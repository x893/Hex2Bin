using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Globalization;

namespace Hex2Bin
{
	enum FileFormat
	{
		Auto,
		Binary,
		Hex,
		Jam
	}

    class Program
    {
		static byte[] Memory = null;
		static uint MemoryLow = uint.MaxValue;
		static uint MemoryHigh = uint.MinValue;
		static uint AddressMax = 0;
		static uint AddressMin = uint.MaxValue;
		static uint BaseAddress = 0;
		static bool ScanOnly = false;
		static byte EmptyValue = 0xFF;

		const uint BLOCK_SIZE = 1024;

		static void usageHelp()
		{
			Console.Write(@"
Hex2Bin [options] in-file.hex|bin|s|jam [out-file]
    -h[elp]             display this text
    -b[ase]=address     set base address (0x... for hex)
    -s[can]             scan file for min/max addresses
    -f[ill]=value       fill value for empty memory
    -mcu=PIC24FJ256     allocate memory ones (256K)
    -mcu=PIC24FJ128     allocate memory ones (128K)
    -bin                force input format to BINARY format
    -hex                force input format to HEX format
    -jam                force input format to JAM format
Process .jam (Microchip format) file and make one image file
        .hex Intel hex format file
        .s   Motorola format file
        .bin binary file
"
				);
		}

		static void Main(string[] args)
        {
            string fileIn = null;
            string fileOut = null;
			int exitCode = 0;
			FileFormat input_type = FileFormat.Auto;
			if (args.Length == 0)
			{
				usageHelp();
				exitCode = 1;
			}
			else
			{
				foreach (string arg in args)
				{
					if (arg.StartsWith("-", StringComparison.InvariantCulture) ||
						arg.StartsWith("/", StringComparison.InvariantCulture)
						)
					{
						#region Process options 

						string[] option = arg.Substring(1).Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
						string value = string.Empty;
						if (option.Length == 2)
							value = option[1].Trim();

						switch (option[0].ToLowerInvariant())
						{
							case "hex":
								input_type = FileFormat.Hex;
								break;
							case "bin":
								input_type = FileFormat.Binary;
								break;
							case "jam":
								input_type = FileFormat.Jam;
								break;
							case "mcu":
								#region Set PIC type 
								switch (value.ToUpperInvariant())
								{
									case "PIC24FJ256":
										Memory = new byte[256 * 1024];
										break;
									case "PIC24FJ128":
										Memory = new byte[128 * 1024];
										break;
									default:
										Console.WriteLine(string.Format("Unknown -mcu {0}", value));
										exitCode = 1;
										break;
								}
								break;
								#endregion
							case "b":
							case "base":
								#region Set base address 
								if (!string.IsNullOrEmpty(value))
								{
									if (value.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
									{	// Hex format 0x....
										if (uint.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out BaseAddress))
											break;
									}
									else
									{
										if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out BaseAddress))
											break;
									}
								}
								exitCode = 1;
								break;
								#endregion
							case "f":
							case "fill":
								#region Set empty memory value
								if (!string.IsNullOrEmpty(value))
								{
									if (value.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
									{	// Hex format 0x
										if (byte.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out EmptyValue))
											break;
									}
									else
									{	// Decimal format
										if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out EmptyValue))
											break;
									}
								}
								exitCode = 1;
								break;
								#endregion
							case "s":
							case "scan":
								#region Set scan file only mode 
								ScanOnly = true;
								break;
								#endregion
							default:
								#region Unknown option 
								Console.WriteLine(string.Format("Unknown option {0}", arg));
								exitCode = 1;
								break;
								#endregion
						}
						#endregion
					}
					else if (fileIn == null)
						fileIn = arg;	// First paramter as input file
					else if (fileOut == null)
						fileOut = arg;	// Second paramter as output file
					else
					{
						Console.WriteLine("Too many files define.");
						exitCode = 1;
					}
					if (exitCode != 0)
						break;
				}

				if (exitCode == 0)
				{
					exitCode = 1;

					if (string.IsNullOrEmpty(fileIn))
						Console.WriteLine("Input file empty.");
					else
					{
						if (!File.Exists(fileIn))
							Console.WriteLine(string.Format("Input file {0} not found.", fileIn));
						else
						{
							string ext = Path.GetExtension(fileIn).ToLowerInvariant();
							switch (input_type)
							{
								case FileFormat.Binary:
									ext = ".bin";
									break;
								case FileFormat.Hex:
									ext = ".hex";
									break;
								case FileFormat.Jam:
									ext = ".jam";
									break;
							}
							switch (ext)
							{
								case ".jam":
									// Process Microchip JAM file
									// Each line is HEX file name
									using (TextReader s = new StreamReader(fileIn))
									{
										string filename;
										while ((filename = s.ReadLine()) != null)
										{
											filename = filename.Trim();
											if (filename.Length == 0)
												continue;
											Console.WriteLine(string.Format("Process file:{0}", filename));
											exitCode = hex2bin(filename);
											if (exitCode != 0)
												break;
										}
									}
									break;

								case ".s":
								case ".hex":
									exitCode = hex2bin(fileIn);
									break;

								case ".bin":
									exitCode = bin2hex(fileIn);
									break;
								default:
									Console.WriteLine("Unknown extension {0}", Path.GetExtension(fileIn).ToLowerInvariant());
									exitCode = -1;
									break;
							}

							#region Save internal memory to binary file 
							if (exitCode == 0 && Memory != null && Memory.Length > 0)
							{
								Console.WriteLine("Address range: 0x{0:X}-0x{1:X}", AddressMin, AddressMax);
								if (!ScanOnly)
								{
									if (fileOut == null)
										fileOut = string.Concat(Path.GetFileNameWithoutExtension(fileIn), ".bin");

									using (Stream s = File.Open(fileOut, File.Exists(fileOut) ? FileMode.Truncate : FileMode.Create, FileAccess.Write))
									{
										s.Write(Memory, 0, (int)(AddressMax - AddressMin + 1));
										s.Close();
									}
								}
							}
							#endregion
						}
					}
				}
			}
            Environment.Exit(exitCode);
        }

		enum MOTOROLA_COMMAND : byte
		{
			CMD_00 = 0x00,
			CMD_01 = 0x01,
			CMD_02 = 0x02,
			CMD_03 = 0x03,
			CMD_04 = 0x04,
			CMD_05 = 0x05,
			CMD_06 = 0x06,
			CMD_07 = 0x07,
			CMD_08 = 0x08,
			CMD_09 = 0x09
		}
		enum INTEL_COMMAND : byte
		{
			UNKNOWN = 0xFF,
			DATA = 0x00,
			EOF = 0x01,
			EXT_SEGMENT_ADDR = 0x02,
			SEGMENT_ADDR = 0x03,
			EXTEND_ADDR = 0x04,
			LINEAR_ADDR = 0x05,
			DATA_LOOP = 0x10
		}

		#region bin2hex(string fileIn) 
		/// <summary>
		/// Convert binary file to hex format
		/// </summary>
		/// <param name="fileIn"></param>
		/// <returns></returns>
		private static int bin2hex(string fileIn)
		{
			uint out_address = BaseAddress;
			uint hex_address = 0;
			int exitCode = 0;

			try
			{
				using (FileStream fs = new FileStream(fileIn, FileMode.Open))
				{
					using (BinaryReader b = new BinaryReader(fs))
					{
						Memory = new byte[fs.Length];
						b.Read(Memory, 0, (int)fs.Length);
						b.Close();
					}
					fs.Close();
				}

				if (Memory != null && Memory.Length > 0)
				{
					int byte_index = 0;
					StringBuilder sb = new StringBuilder(50);
					uint cs;

					string fileOut = string.Concat(Path.GetFileNameWithoutExtension(fileIn), ".hex");

					using (FileStream fs = new FileStream(fileOut, FileMode.Create, FileAccess.Write))
					{
						using (TextWriter w = new StreamWriter(fs))
						{
							while (byte_index < Memory.Length)
							{
								sb.Remove(0, sb.Length);
								if (hex_address != (out_address & 0xFFFF0000))
								{
									cs = 2 + (uint)INTEL_COMMAND.EXTEND_ADDR + ((out_address >> 16) & 0xFF) + ((out_address >> 24) & 0xFF);
									sb.AppendFormat(":020000{0:X2}{1:X4}{2:X2}", (uint)INTEL_COMMAND.EXTEND_ADDR, (out_address >> 16) & 0xFFFF, ((cs ^ 0xFF) + 1) & 0xFF);
									w.WriteLine(sb.ToString());
									hex_address = (out_address & 0xFFFF0000);
									sb.Remove(0, sb.Length);
								}

								uint byte_count = Math.Min((uint)16, (uint)(Memory.Length - byte_index));
								sb.AppendFormat(":{0:X2}{1:X4}{2:X2}", byte_count, out_address & 0xFFFF, (uint)INTEL_COMMAND.DATA);
								cs = byte_count + (out_address & 0xFF) + (out_address >> 8 & 0xFF) + (uint)INTEL_COMMAND.DATA;
								while (byte_count > 0)
								{
									byte data = Memory[byte_index++];
									cs += data;
									sb.AppendFormat("{0:X2}", data);
									out_address++;
									--byte_count;
								}
								sb.AppendFormat("{0:X2}", ((cs ^ 0xFF) + 1) & 0xFF);
								w.WriteLine(sb.ToString());
							}
							w.WriteLine(":00000001FF");
							w.Close();
						}
						fs.Close();
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				exitCode = 1;
			}
			Memory = null;
			return exitCode;
		}
		#endregion

		#region hex2bin(string fileIn) 
		/// <summary>
		/// Convert HEX|S file to internal byte array
		/// </summary>
		/// <param name="fileIn"></param>
		/// <returns></returns>
		private static int hex2bin(string fileIn)
        {
			uint extend_address = 0, start_address = 0, segment_address = 0, linear_address = 0;
            string line;
            int lineNumber = 0;
            bool fail = false;
            uint count = 0, address = 0;
            byte data = 0, checksum;
			int idx = 0;
			INTEL_COMMAND command = INTEL_COMMAND.UNKNOWN;

            using (TextReader s = new StreamReader(fileIn))
            {
                while ((line = s.ReadLine()) != null)
                {
                    ++lineNumber;
                    line = line.Trim();
                    if (line.Length == 0)
                        continue;

					if (line.StartsWith("S"))
					{
						#region Motorola format 
						// Stccaaaaaaadd...ddss
						if (line.Length < 9)
						{
							fail = true;
						}
						else
						{
							fail |= !byte.TryParse(line.Substring(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data);
							MOTOROLA_COMMAND m_command = (MOTOROLA_COMMAND)data;
							switch (m_command)
							{
								case MOTOROLA_COMMAND.CMD_01:
									fail |= !uint.TryParse(line.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out count);
									fail |= !uint.TryParse(line.Substring(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
									command = INTEL_COMMAND.DATA_LOOP;
									idx = 8;
									count -= 3;
									break;
								case MOTOROLA_COMMAND.CMD_02:
									if (line.Length < 11)
									{
										fail = true;
									}
									else
									{
										fail |= !uint.TryParse(line.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out count);
										fail |= !uint.TryParse(line.Substring(4, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
									}
									command = INTEL_COMMAND.DATA_LOOP;
									idx = 10;
									count -= 4;
									break;
								case MOTOROLA_COMMAND.CMD_03:
									if (line.Length < 13)
									{
										fail = true;
									}
									else
									{
										fail |= !uint.TryParse(line.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out count);
										fail |= !uint.TryParse(line.Substring(4, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
									}
									command = INTEL_COMMAND.DATA_LOOP;
									idx = 12;
									count -= 5;
									break;
								case MOTOROLA_COMMAND.CMD_00:
								case MOTOROLA_COMMAND.CMD_04:
								case MOTOROLA_COMMAND.CMD_05:
								case MOTOROLA_COMMAND.CMD_06:
									fail = true;
									break;
								case MOTOROLA_COMMAND.CMD_07:
								case MOTOROLA_COMMAND.CMD_08:
								case MOTOROLA_COMMAND.CMD_09:
									continue;
							}
						}
						#endregion
					}
					else if (line.StartsWith(":"))
					{
						#region Intel format 
						if (line.Length < 11)
						{
							fail = true;
						}
						else
						{
							fail |= !uint.TryParse(line.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out count);
							fail |= !uint.TryParse(line.Substring(3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
							fail |= !byte.TryParse(line.Substring(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data);
							command = (INTEL_COMMAND)data;
							fail |= !byte.TryParse(line.Substring(line.Length - 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out checksum);
						}
						#endregion
					}
					else
						continue;	// Ignore line

                    if (fail)
                    {
                        Console.WriteLine(string.Format("Can't parse line {0}", lineNumber));
                        break;
                    }

					switch (command)
					{
						case INTEL_COMMAND.EOF: // End of File
							return 0;
						case INTEL_COMMAND.DATA:
							#region Data Record
							idx = 9;
							goto data_loop;
						case INTEL_COMMAND.DATA_LOOP:
						data_loop:
							for (; !fail && count > 0; --count)
							{
								if (line.Length < idx + 2)
								{
									Console.WriteLine(string.Format("Data record too short at line {0}", lineNumber));
									fail = true;
								}
								else
									fail = !byte.TryParse(line.Substring(idx, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data);

								if (!fail)
									writeToMemory(segment_address + extend_address + address - BaseAddress, data);

								address++;
								idx += 2;
							}
							break;
							#endregion
						case INTEL_COMMAND.EXT_SEGMENT_ADDR: // Extended Segment Address Record
							#region Extended segment address record
							if (count != 2 || line.Length != 15)
							{
								Console.WriteLine(string.Format("Bad Extended segment address record line {0}.", lineNumber));
							}
							else
							{
								fail |= !uint.TryParse(line.Substring(9, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out segment_address);
								if (fail)
									Console.WriteLine(string.Format("Bad Start Address records line {0}.", lineNumber));
								else
									segment_address <<= 4;
							}
							break;
							#endregion
						case INTEL_COMMAND.SEGMENT_ADDR:
							#region Start Segment Address Record
							if (count != 4)
							{
								Console.WriteLine(string.Format("Bad Start Segment records line {0}.", lineNumber));
							}
							else
							{
								fail |= !uint.TryParse(line.Substring(9, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out start_address);
								if (fail)
									Console.WriteLine(string.Format("Bad Start Segment records line {0}.", lineNumber));
								else
									Console.WriteLine(string.Format("Start Segment: {0:X}.", start_address));
							}
							break;
							#endregion
						case INTEL_COMMAND.EXTEND_ADDR:
							#region Extended Linear Address Record
							if (line.Length != 15)
							{
								Console.WriteLine(string.Format("Bad Extended Address records line {0}.", lineNumber));
								fail = true;
							}
							else
							{
								fail |= !uint.TryParse(line.Substring(9, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out extend_address);
								if (!fail)
									extend_address = extend_address << 16;
							}
							break;
							#endregion
						case INTEL_COMMAND.LINEAR_ADDR:
							#region Start Linear Address Record
							if (count != 4)
							{
								Console.WriteLine(string.Format("Bad Linear Address record line {0}.", lineNumber));
							}
							else
							{
								fail |= !uint.TryParse(line.Substring(9, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out linear_address);
								if (fail)
									Console.WriteLine(string.Format("Bad Linear Address record line {0}.", lineNumber));
								else
									Console.WriteLine(string.Format("Linear Address: 0x{0:X}", linear_address));
							}
							break;
							#endregion
						default:
							Console.WriteLine(string.Format("Bad command {0} at line {1}.", command, lineNumber));
							fail = true;
							break;
					}
                    if (fail)
                        break;
                }
            }
            return fail ? 1 : 0;
        }
		#endregion

		#region writeToMemory(uint address, byte data) 
		private static void writeToMemory(uint address, byte data)
		{
			if (address < MemoryLow || address >= MemoryHigh)
			{
				uint block = address / BLOCK_SIZE;
				uint low = Math.Min(MemoryLow, block * BLOCK_SIZE);
				uint high = Math.Max(MemoryHigh, (block + 1) * BLOCK_SIZE);
				uint idx;
				if (Memory == null)
				{
					Memory = new byte[high - low];
					MemoryLow = low;
					idx = MemoryLow;
				}
				else
				{
					byte[] memory = new byte[high - low];
					Memory.CopyTo(memory, MemoryLow - low);
					Memory = memory;
					idx = low;
					while (idx < MemoryLow)
						Memory[idx++ - low] = EmptyValue;
					MemoryLow = low;
					idx = MemoryHigh;
				}
				while (idx < high)
					Memory[idx++ - low] = EmptyValue;
				MemoryHigh = high;
			}
			AddressMax = Math.Max(AddressMax, address);
			AddressMin = Math.Min(AddressMin, address);
			Memory[address - MemoryLow] = data;
		}
		#endregion
	}
}
