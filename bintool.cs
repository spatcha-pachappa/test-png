using System;
using System.IO;
using System.Text;

public class TextToBinaryConverter
{
	public class Options
	{
		// 将来拡張用（今は空でもOK）
	}

	public static void Convert(string inputPath, string outputPath, Options options)
	{
		using (var input = new StreamReader(inputPath, Encoding.ASCII))
		using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
		{
			int highNibble = -1;

			while (true)
			{
				int value = input.Read();
				if (value == -1)
				{
					break;
				}

				var c = (char)value;

				// 区切り文字は無視
				if (IsSeparator(c))
				{
					continue;
				}

				int hex = HexToInt(c);

				if (hex < 0)
				{
					throw new FormatException(string.Format("Invalid character: '{0}'", c));
				}

				if (highNibble < 0)
				{
					highNibble = hex;
				}
				else
				{
					byte b = (byte)((highNibble << 4) | hex);
					output.WriteByte(b);
					highNibble = -1;
				}
			}

			// 桁数が奇数
			if (0 <= highNibble)
			{
				throw new FormatException("Odd number of hex digits");
			}
		}
	}

	private static bool IsSeparator(char c)
	{
		return c == ' '
			|| c == '\t'
			|| c == '\r'
			|| c == '\n'
			|| c == ',';
	}

	private static int HexToInt(char c)
	{
		if (('0' <= c) && (c <= '9'))
		{
			return c - '0';
		}

		if (('A' <= c) && (c <= 'F'))
		{
			return c - 'A' + 10;
		}

		if (('a' <= c) && (c <= 'f'))
		{
			return c - 'a' + 10;
		}

		return -1;
	}
}

public class BinaryToTextConverter
{
	public enum SeparatorType
	{
		None,
		Space,
		Tab,
	}

	public class Options
	{
		public SeparatorType Separator {get; set;}
		public bool InsertLineBreak {get; set;}
		public int BytesPerLine {get; set;}
		public bool UpperCase {get; set;}

		public Options()
		{
			this.Separator = SeparatorType.Space;
			this.InsertLineBreak = true;
			this.BytesPerLine = 16;
			this.UpperCase = true;
		}
	}

	public static void Convert(string inputPath, string outputPath, Options options)
	{
		using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
		using (var output = new StreamWriter(outputPath, false, Encoding.ASCII))
		{
			var buffer = new byte[4096];
			int bytesRead = 0;
			int countInLine = 0;

			var separator = "";
			switch (options.Separator)
			{
				case SeparatorType.Space:
					separator = " ";
					break;
				case SeparatorType.Tab:
					separator = "\t";
					break;
			};

			var format = options.UpperCase ? "X2" : "x2";

			while (0 < (bytesRead = input.Read(buffer, 0, buffer.Length)))
			{
				for (int i = 0; i < bytesRead; i ++)
				{
					// 16進数に変換
					var hex = buffer[i].ToString(format);
					output.Write(hex);

					countInLine ++;

					// 区切り
					bool isLastByte = (i == bytesRead - 1) && (input.Position == input.Length);
					if (!isLastByte)
					{
						if (options.InsertLineBreak && (options.BytesPerLine <= countInLine))
						{
							output.WriteLine();
							countInLine = 0;
						}
						else if (options.Separator != SeparatorType.None)
						{
							output.Write(separator);
						}
					}
				}
			}
		}
	}
}

public class Program
{
	public static int Main(string[] args)
	{
		if (args.Length < 3)
		{
			PrintUsage();
			return 1;
		}

		var mode = args[0];
		var inputPath = args[1];
		var outputPath = args[2];

		try
		{
			switch (mode)
			{
				case "b2t":
				{
					var options = new BinaryToTextConverter.Options();
					ParseEncodeOptions(args, 3, options);

					BinaryToTextConverter.Convert(inputPath, outputPath, options);
					break;
				}

				case "t2b":
				{
					var options = new TextToBinaryConverter.Options();

					TextToBinaryConverter.Convert(inputPath, outputPath, options);
					break;
				}

				default:
				{
					throw new ArgumentException(string.Format("Unknown mode: {0}", mode));
				}
			}

			return 0;
		}
		catch (Exception exc)
		{
			Console.Error.WriteLine(string.Format("Error: {0}", exc.Message));
			return 1;
		}
	}

	private static void ParseEncodeOptions(string[] args, int startIndex, BinaryToTextConverter.Options options)
	{
		for (int i = startIndex; i < args.Length; i ++)
		{
			var arg = args[i];

			switch (arg)
			{
				case "--sep":
				{
					if (args.Length <= i + 1)
					{
						throw new ArgumentException("--sep requires a value");
					}

					var value = args[++ i].ToLower();

					switch (value)
						{
							case "space":
								options.Separator = BinaryToTextConverter.SeparatorType.Space;
								break;
							case "tab":
								options.Separator = BinaryToTextConverter.SeparatorType.Tab;
								break;
							case "none":
								options.Separator = BinaryToTextConverter.SeparatorType.None;
								break;
							default:
								throw new ArgumentException(string.Format("Invalid separator: {0}", value));
						}
					break;
				}

				case "--no-break":
				{
					options.InsertLineBreak = false;
					break;
				}

				case "--width":
				{
					if (args.Length <= i + 1)
					{
						throw new ArgumentException("--width requires a value");
					}

					int width = 0;
					if (!int.TryParse(args[++ i], out width) || (width <= 0))
					{
						throw new ArgumentException("Invalid width");
					}

					options.BytesPerLine = width;
					break;
				}

				case "--lower":
				{
					options.UpperCase = false;
					break;
				}

				default:
				{
					throw new ArgumentException(string.Format("Unknown option: {0}", arg));
				}
			}
		}
	}

	private static void PrintUsage()
	{
		Console.WriteLine("Usage:");
		Console.WriteLine("  bintool t2b <input> <output>");
		Console.WriteLine();
		Console.WriteLine("  bintool b2t <input> <output> [options]");
		Console.WriteLine();
		Console.WriteLine("    Options:");
		Console.WriteLine("      --sep space|tab|none   Separator between bytes (default: space)");
		Console.WriteLine("      --no-break             Do not insert line breaks");
		Console.WriteLine("      --width N              Bytes per line (default: 16)");
		Console.WriteLine("      --lower                Use lowercase hex (default: uppercase)");
	}
}
