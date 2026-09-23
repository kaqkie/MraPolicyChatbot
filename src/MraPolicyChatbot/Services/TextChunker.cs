using System.Text.RegularExpressions;

namespace MraPolicyChatbot.Services;

// Phase 5: splits extracted document text into overlapping, word-count-
// based chunks. Prefers natural boundaries (paragraphs, then sentences) so
// chunks don't cut sentences in half where avoidable. Plain string
// processing only — no AI, no embeddings, no semantic chunking.
public static class TextChunker
{
    public static List<string> ChunkText(string text, int chunkSize = 400, int overlap = 50)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var sentences = SplitIntoSentences(text);
        if (sentences.Count == 0)
        {
            return new List<string>();
        }

        // Allow a chunk to drift a bit above chunkSize before forcing a
        // break, so typical chunks land in the requested 300-500 word
        // range when chunkSize is the default 400.
        var maxBeforeBreak = chunkSize + (chunkSize / 4);

        var chunks = new List<string>();
        var currentWords = new List<string>();

        foreach (var sentence in sentences)
        {
            var sentenceWords = SplitIntoWords(sentence);

            // Fallback for a single "sentence" that's already too long on
            // its own (e.g. extraction produced a long run with no
            // punctuation) — hard-split it so no chunk grows unbounded.
            if (sentenceWords.Count > chunkSize)
            {
                foreach (var piece in HardSplit(sentenceWords, chunkSize))
                {
                    currentWords.AddRange(piece);
                    if (currentWords.Count >= chunkSize)
                    {
                        chunks.Add(string.Join(' ', currentWords));
                        currentWords = TakeOverlap(currentWords, overlap);
                    }
                }
                continue;
            }

            if (currentWords.Count > 0 && currentWords.Count + sentenceWords.Count > maxBeforeBreak)
            {
                chunks.Add(string.Join(' ', currentWords));
                currentWords = TakeOverlap(currentWords, overlap);
            }

            currentWords.AddRange(sentenceWords);

            if (currentWords.Count >= chunkSize)
            {
                chunks.Add(string.Join(' ', currentWords));
                currentWords = TakeOverlap(currentWords, overlap);
            }
        }

        if (currentWords.Count > 0)
        {
            // Don't emit a trailing chunk that's just leftover overlap with
            // no new content in it.
            var isJustOverlap = chunks.Count > 0 && currentWords.Count <= overlap;
            if (!isJustOverlap)
            {
                chunks.Add(string.Join(' ', currentWords));
            }
        }

        return chunks;
    }

    private static List<string> TakeOverlap(List<string> words, int overlap)
    {
        if (overlap <= 0 || words.Count <= overlap)
        {
            return new List<string>();
        }

        return words.Skip(words.Count - overlap).ToList();
    }

    private static IEnumerable<List<string>> HardSplit(List<string> words, int size)
    {
        for (var i = 0; i < words.Count; i += size)
        {
            yield return words.Skip(i).Take(size).ToList();
        }
    }

    private static List<string> SplitIntoWords(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static List<string> SplitIntoSentences(string text)
    {
        var paragraphs = text
            .Replace("\r\n", "\n")
            .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        var sentences = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var normalized = Regex.Replace(paragraph, @"\s+", " ").Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            var parts = Regex.Split(normalized, @"(?<=[.!?])\s+");
            sentences.AddRange(parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        return sentences;
    }
}
