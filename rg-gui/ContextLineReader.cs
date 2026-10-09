using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace rg_gui
{
    public static class ContextLineReader
    {
        public const long MAX_FILE_SIZE = 50L * 1024 * 1024;

        static ContextLineReader()
        {
            // GBK is not available in .NET by default.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>
        /// Reads a file's lines, numbered the same way as ripgrep (lines end at '\n').
        /// Returns an error message instead if the file can't be used for context lines.
        /// </summary>
        public static (string[]? lines, string? error) ReadLines(string filePath, bool useGbk)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    return (null, "file not found");
                }

                if (fileInfo.Length > MAX_FILE_SIZE)
                {
                    return (null, $"file is larger than {MAX_FILE_SIZE / 1024 / 1024} MB");
                }

                // Without GBK, use the byte order mark if there is one, otherwise UTF-8.
                var encoding = useGbk ? Encoding.GetEncoding("GBK") : new UTF8Encoding(false);
                string text;
                using (var reader = new StreamReader(filePath, encoding, detectEncodingFromByteOrderMarks: true))
                {
                    text = reader.ReadToEnd();
                }

                if (text.Contains('\0'))
                {
                    return (null, "file appears to be binary");
                }

                var lines = text.Split('\n');

                // A trailing newline ends the last line rather than starting a new one.
                if (lines.Length > 1 && text.EndsWith('\n'))
                {
                    lines = lines[..^1];
                }

                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].EndsWith('\r'))
                    {
                        lines[i] = lines[i][..^1];
                    }
                }

                return (lines, null);
            }
            catch (Exception)
            {
                return (null, "file could not be read");
            }
        }

        /// <summary>
        /// Returns true if every match line still exists in the file with the same text that ripgrep found.
        /// </summary>
        public static bool MatchesFile(string[] lines, IEnumerable<(int lineNumber, string lineContent)> matches)
        {
            return matches.All(x => x.lineNumber >= 1 && x.lineNumber <= lines.Length && lines[x.lineNumber - 1].TrimEnd() == x.lineContent.TrimEnd());
        }

        /// <summary>
        /// Expands each match line by the given number of lines before and after (within the file),
        /// and merges blocks that overlap or touch. Blocks are returned in line order.
        /// </summary>
        public static List<(int start, int end)> GetBlocks(IEnumerable<int> matchLines, int linesBefore, int linesAfter, int lineCount)
        {
            var blocks = new List<(int start, int end)>();

            foreach (var line in matchLines.Distinct().OrderBy(x => x))
            {
                var start = (int)Math.Max(1L, (long)line - linesBefore);
                var end = (int)Math.Min(lineCount, (long)line + linesAfter);

                if (blocks.Count > 0 && start <= blocks[^1].end + 1)
                {
                    blocks[^1] = (blocks[^1].start, Math.Max(blocks[^1].end, end));
                }
                else
                {
                    blocks.Add((start, end));
                }
            }

            return blocks;
        }
    }
}
