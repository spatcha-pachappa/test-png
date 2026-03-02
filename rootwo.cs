using System;
using System.IO;

public static class Program
{
	public static int Main(string[] args)
	{
		if (args.Length != 3)
		{
			Console.WriteLine("Usage: program <input> <output> <mode:add|sub>");
			return 1;
		}

		var inputFile = args[0];
		var outputFile = args[1];
		var mode = args[2];

		bool addMode = (mode.ToLower() == "add");
		bool subMode = (mode.ToLower() == "sub");

		if (!addMode && !subMode)
		{
			Console.WriteLine("Mode must be 'add' or 'sub'.");
			return 2;
		}

		// ÉãÅ[Ég2è¨êîì_à»â∫16åÖ
		byte[] offsets = new byte[]{1, 4, 1, 4, 2, 1, 3, 5, 6, 2, 3, 7, 3, 0, 9, 5, };
		int offsetLen = offsets.Length;

		const int bufferSize = 8192;

		using (var input = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
		using (var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
		{
			byte[] buffer = new byte[bufferSize];

			long totalRead = 0;
			int read;

			while (0 < (read = input.Read(buffer, 0, bufferSize)))
			{
				for (int i = 0; i < read; i ++)
				{
					int offset = offsets[(int)((totalRead + i) % offsetLen)];
					if (addMode)
					{
						buffer[i] = (byte)((buffer[i] + offset) & 0xFF);
					}
					else
					{
						buffer[i] = (byte)((buffer[i] - offset) & 0xFF);
					}
				}
				output.Write(buffer, 0, read);
				totalRead += read;
			}
		}

		Console.WriteLine(addMode ? "â¡éZèàóùäÆóπ" : "å∏éZèàóùäÆóπ");
		return 0;
	}
}
