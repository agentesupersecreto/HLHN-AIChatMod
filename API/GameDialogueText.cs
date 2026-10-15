using System;
using System.Text;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Prepares a line of the game's own dialogue for the TTS endpoint.
    ///
    /// The native lines are written for a text bubble, not for a voice, and they are fed to the
    /// endpoint with no AI processing in between. Two things have to be removed before a voice reads
    /// them: bracketed hints the game shows the player ("[sexTalk] button to change position") and the
    /// markup the writers used for emphasis ("*soft moan*", "~sigh~"). Quotation marks go too, since
    /// a voice should not read them out loud, while an apostrophe inside a word ("don't") is kept:
    /// there it is part of the word, not punctuation. Everything else - including a trailing "..." -
    /// is left alone, because it is part of how the character talks.
    ///
    /// Pure BCL: no Unity types, so the exact text that would be sent can be asserted outside the game.
    /// </summary>
    public static class GameDialogueText
    {
        /// <summary>
        /// Cleans a native line, or returns an empty string when nothing speakable is left (the caller
        /// then skips the request instead of paying for a line of punctuation).
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // A bracketed run is an on-screen hint ("[sexTalk]", "[F] to open"), not speech.
                if (c == '[')
                {
                    int close = text.IndexOf(']', i + 1);
                    if (close > i) { i = close; continue; }

                    // Unterminated bracket: drop just the bracket and keep the text.
                    continue;
                }

                // Emphasis markup: *soft moan*, _whispering_, ~sigh~.
                if (c == '*' || c == '_' || c == '~') continue;

                // Quoted speech keeps its words but loses the quotation marks, so the voice does not
                // read them out loud. An apostrophe inside a word is not punctuation but part of the
                // word ("don't", "she's"), and removing it would change what the voice says.
                if (c == '"' || c == '\'')
                {
                    bool insideWord = i > 0 && i + 1 < text.Length &&
                                      char.IsLetter(text[i - 1]) && char.IsLetter(text[i + 1]);
                    if (insideWord) sb.Append(c);
                    continue;
                }

                // Rich-text tags (<i>, </size>) are markup too; a bare "<" is kept.
                if (c == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i && close - i <= 24) { i = close; continue; }
                }

                sb.Append(c);
            }

            return CollapseWhitespace(sb.ToString());
        }

        /// <summary>
        /// Tag every "Sex-Talk" line is sent with, so the voice performs it as a moan instead of
        /// reading it flat.
        ///
        /// fish.audio takes bracket tags before the text they apply to (<c>[excited] We did it!</c>)
        /// and reads everything after the tag in that style, so the tag belongs at the very start of
        /// the line. It is a free-form tag rather than one of the documented core ones
        /// (<c>[whisper]</c>, <c>[laugh]</c>, <c>[sigh]</c>, <c>[gasp]</c>, <c>[pause]</c>), which is
        /// allowed - the model accepts any description in brackets - but free-form tags are the ones
        /// that can vary between generations. If it ever sounds wrong or gets read out loud, this is
        /// the single place to change it.
        /// </summary>
        public const string SexTalkTag = "[moaning]";

        /// <summary>
        /// Adds <see cref="SexTalkTag"/> to a Sex-Talk line. It must be called <b>after</b>
        /// <see cref="Sanitize"/>: the sanitizer drops bracketed runs (they are on-screen hints in the
        /// game's text), so a tag added before it would be removed again. Idempotent, so a line that
        /// already carries the tag is not tagged twice.
        /// </summary>
        public static string WithSexTalkTag(string sanitized)
        {
            if (string.IsNullOrEmpty(sanitized)) return "";
            if (sanitized.StartsWith(SexTalkTag, StringComparison.OrdinalIgnoreCase)) return sanitized;

            return SexTalkTag + " " + sanitized;
        }

        /// <summary>True when the cleaned line has something a voice can actually say.</summary>
        public static bool HasSpeakableText(string sanitized)
        {
            if (string.IsNullOrEmpty(sanitized)) return false;

            foreach (char c in sanitized)
                if (char.IsLetterOrDigit(c)) return true;

            return false;
        }

        /// <summary>
        /// Collapses the runs of spaces the removals leave behind, and trims the ends. Line breaks are
        /// flattened to a single space: a voice should not read a pause into them.
        /// </summary>
        private static string CollapseWhitespace(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool pendingSpace = false;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
