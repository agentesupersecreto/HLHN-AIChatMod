using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Pure text logic behind the "go to a place" command: it turns a player sentence
    /// ("go to the kitchen", "vete a la cocina", "clean the bathroom", "dust the living room")
    /// or a bare place name into tokens and scores those tokens against the spots the game itself
    /// knows about (chore assets, placed chore spots, user-defined locations).
    ///
    /// Two vocabularies are used, because the house names its spots in two different ways:
    ///   * <see cref="AreaSynonyms"/> — the rooms ("kitchen", "bathroom", "upstairs"...).
    ///   * <see cref="ChoreSynonyms"/> — the housework that most routine spots are named after
    ///     ("WashingPlates", "DustingLivingRoom", "SweepingHallway"...).
    ///
    /// The room the player names weighs more than the chore, and a spot matching both is the best
    /// answer, so "dust the dining room" picks a dining-room spot rather than the closest dusting
    /// one. No UnityEngine types are used here on purpose: the matching rules are the part worth
    /// testing in isolation from the running game.
    /// </summary>
    public static class PlaceMatcher
    {
        /// <summary>
        /// Verb phrases that introduce a destination, longest first so "lie down on" wins over
        /// "lie". Both English (the game's own asset language) and Spanish (many players, and the
        /// game's descriptions use it) are listed; Normalize() has already stripped accents.
        /// The housework verbs at the end of each language cover the chore-style orders
        /// ("sweep the hallway", "barre el pasillo").
        /// </summary>
        private static readonly string[] MovePhrases =
        {
            // English (longest first)
            "lie down on the", "lie down on", "lie down in", "lay down on the", "lay down on",
            "sit down on the", "sit down on", "sit next to", "sit down at",
            "go to the", "go into the", "go into", "go in", "go to",
            "walk to the", "walk into the", "walk to", "walk into", "walk over to",
            "head over to", "head to the", "head to", "move to the", "move to",
            "wait in the", "wait in", "wait at the", "wait at", "wait by",
            "stand in the", "stand in", "stand at the", "stand at", "stand next to",
            "stay in the", "stay in", "stay at the", "stay at", "stay by",
            "sit on the", "sit on", "sit in the", "sit in", "sit at the", "sit at", "sit by",
            "lie on the", "lie on", "lie in the", "lie in", "lie down",
            "sleep in the", "sleep in", "sleep on the", "sleep on", "go to sleep",
            "lay on the", "lay on", "get in the", "get into the",
            "hang out in the", "hang out in", "hang out at", "relax in the", "relax in",
            "go", "walk", "head", "move", "stand", "sit", "lie", "sleep", "wait",
            // English housework: the verbs the game's own spot names are built from. Kept after
            // the movement verbs so "go wash the dishes" is still read from "go".
            "take out the", "take out", "put away the", "put away", "pick up the", "pick up",
            "do the", "do my", "do your", "make the", "make some",
            "look around", "listen to music",
            "clean up", "tidy up", "wash up", "wipe down", "dry up",
            "clean", "wash", "dust", "sweep", "broom", "mop", "vacuum", "scrub", "wipe", "tidy",
            "iron", "fold", "water", "cook", "bake", "hoover", "pray", "smoke", "masturbate",
            // Spanish (longest first)
            "recuestate en el", "recuestate en la", "recuestate en", "acuestate en el", "acuestate en la",
            "acuestate en", "tumbate en el", "tumbate en la", "tumbate en", "echate en el", "echate en",
            "sientate en el", "sientate en la", "sientate en", "sientate a", "sientate",
            "vete a la", "vete al", "vete a", "ve a la", "ve al", "ve a", "vete", "dirigete hacia",
            "dirigete a", "dirigete al", "acercate a la", "acercate al", "acercate a",
            "anda a la", "anda al", "anda a", "anda para", "camina hacia", "camina a la", "camina al", "camina a",
            "espera en el", "espera en la", "espera en", "quedate en el", "quedate en la", "quedate en",
            "parate en el", "parate en la", "parate en", "ponte en el", "ponte en la", "ponte en",
            "duerme en el", "duerme en la", "duerme en", "duerme", "a dormir en", "a dormir",
            "sube al", "sube a la", "sube a", "baja al", "baja a la", "baja a",
            "espera", "quedate", "sube", "baja", "ve", "anda", "camina",
            // Spanish housework (imperative and infinitive are both listed: the matcher compares
            // whole words, so "limpia" never matches "limpiar").
            "quita el polvo", "quitar el polvo", "haz la", "haz el", "haz las", "haz los",
            "pasa la", "pasa el",
            "limpia", "limpiar", "barre", "barrer", "friega", "fregar", "lava", "lavar",
            "trapea", "trapear", "aspira", "aspirar", "riega", "regar", "cocinar", "escuchar musica",
            "reza", "rezar", "fuma", "fumar", "masturbarse", "masturbar", "mira alrededor",
            "planchar", "tiende", "tender", "dobla", "doblar", "recoge", "recoger",
            "ordena", "ordenar"
        };

        /// <summary>
        /// Words dropped from an extracted destination ("go to the kitchen please" -> "kitchen").
        /// </summary>
        private static readonly HashSet<string> StopWords = new HashSet<string>
        {
            "the", "a", "an", "to", "into", "in", "on", "at", "by", "my", "your",
            "and", "then", "please", "now", "over", "down", "up", "there", "here", "for",
            "while", "awhile", "bit", "minute", "moment", "second", "hour", "later", "sometimes",
            "la", "el", "los", "las", "al", "un", "una", "unos", "unas", "de", "del", "para",
            "mi", "mis", "tu", "tus", "su", "sus", "por", "favor", "ahora", "alli", "ahi",
            "hacia", "con", "que", "y", "o", "se", "te", "me"
        };

        /// <summary>
        /// Rooms and areas of the house, one concept per group: a word found in a request pulls in
        /// every other word of its group, so "living room" also matches a spot named "SalonChill".
        /// Both languages are kept because the game's asset names and descriptions mix English and
        /// Spanish. Every group is a single concept on purpose: the room and its furniture are
        /// separate groups, so asking for one piece of furniture cannot drag in another spot of the
        /// same room ("sit on the sofa" must not match the spot that watches TV).
        /// </summary>
        private static readonly string[][] AreaSynonyms =
        {
            new[] { "kitchen", "cocina" },
            new[] { "counter", "worktop", "encimera", "fregadero" },
            new[] { "cooker", "stove", "oven", "horno", "microwave", "microondas", "kettle" },
            new[] { "fridge", "refrigerator", "nevera", "heladera", "refrigerador" },
            new[] { "pantry", "despensa" },

            new[] { "bathroom", "bath", "bathtub", "tub", "shower", "showering", "toilet", "wc",
                    "sink", "basin", "lavabo", "banera", "bano", "banarse", "ducha", "ducharse",
                    "duchando", "inodoro", "retrete", "vater", "servicio", "pee" },

            // The room and the furniture the player may name for it («go to the bedroom» has to reach
            // a spot the game spells «BedRoom»), while sleeping itself is a chore below.
            new[] { "bedroom", "dormitorio", "habitacion", "cuarto", "pieza", "alcoba", "bed", "pillow",
                    "almohada", "blanket", "manta", "cama" },

            new[] { "living", "livingroom", "lounge", "salon", "sala", "estar", "recibidor" },
            new[] { "sofa", "couch", "settee", "sillon" },
            new[] { "tv", "television", "tele", "televisor", "watch", "watching", "ver", "viendo" },

            new[] { "dining", "dinning", "dinnerroom", "dinner", "table", "mesa", "comedor" },
            new[] { "chair", "stool", "bench", "silla", "banco", "taburete" },

            new[] { "outside", "outdoor", "outdoors", "exterior", "afuera", "fuera" },
            new[] { "garden", "yard", "jardin", "patio", "terrace", "terraza", "balcony", "balcon",
                    "porch", "veranda" },
            new[] { "sun", "sunbathe", "sunbathing", "sol" },
            new[] { "pool", "piscina" },
            new[] { "street", "calle" },

            new[] { "hall", "hallway", "corridor", "pasillo", "vestibulo" },
            new[] { "entrance", "door", "exit", "entrada", "puerta", "salida" },
            new[] { "stairs", "staircase", "escalera", "escaleras", "peldaños" },
            new[] { "upstairs", "upstair", "arriba", "planta alta", "piso de arriba" },
            new[] { "downstairs", "downstair", "abajo", "planta baja", "piso de abajo" },

            new[] { "office", "study", "work", "working", "job", "computer", "desk", "oficina", "despacho",
                    "trabajar", "trabajo", "ordenador", "computadora", "escritorio" },
            new[] { "garage", "car", "garaje", "cochera", "auto", "coche" },
            new[] { "basement", "attic", "storage", "sotano", "atico", "trastero", "almacen" },
            new[] { "closet", "wardrobe", "armario", "ropero", "vestidor" },
            new[] { "mirror", "espejo" },
            new[] { "gym", "gimnasio", "yoga", "pesas" }
        };

        /// <summary>
        /// The housework the routines name most of their spots after ("WashingPlates",
        /// "DustingLivingRoom", "SweepingHallway", "CheckingClothBasement"...). A chore word in a
        /// request pulls in its whole group, so "wash the dishes" reaches a spot called
        /// "WashingPlates"; the laundry and the dishes share a group because the game mixes
        /// "washing" between the two and the scoring sorts them out.
        /// </summary>
        private static readonly string[][] ChoreSynonyms =
        {
            new[] { "clean", "cleaning", "cleanup", "tidy", "tidying", "housework", "chores",
                    "limpiar", "limpia", "limpieza", "ordenar", "ordena", "recoger", "recoge" },

            new[] { "dust", "dusting", "duster", "polvo", "desempolvar" },
            new[] { "sweep", "sweeping", "broom", "brooming", "barrer", "barre", "escoba", "barrido" },
            new[] { "mop", "mopping", "scrub", "scrubbing", "scrubing", "wipe", "wiping",
                    "trapear", "trapea", "fregona", "fregar", "friega", "fregado", "refregar" },
            new[] { "vacuum", "vacuuming", "hoover", "aspirar", "aspira", "aspiradora" },
            new[] { "water", "watering", "plant", "plants", "flower", "flowers",
                    "regar", "riega", "planta", "plantas", "flores", "maceta", "macetas" },
            // Cooking and eating share a group: the house has one spot where meals happen, and
            // "make something to eat" has to reach it.
            new[] { "cook", "cooking", "bake", "baking", "eat", "eating", "breakfast", "lunch", "meal",
                    "cocinar", "cocinando", "cocine", "hornear", "comer", "comiendo", "desayuno",
                    "almuerzo", "cena" },

            // Washing up and the laundry are separate chores even though the game names both
            // "washing": sharing one group made a spot called "WashingPlates" outscore the actual
            // laundry spot for "do the laundry".
            new[] { "wash", "washing", "washed", "washer", "dishwasher", "dishes", "dish", "plate",
                    "plates", "vajilla", "platos", "plato", "lavar", "lava", "lavado" },
            new[] { "laundry", "cloth", "clothes", "clothing", "dryer", "iron", "ironing", "fold",
                    "folding", "lavadora", "lavadero", "tender", "tiende", "tendedero", "ropa",
                    "colada", "planchar", "plancha", "doblar" },

            // Sleeping is a chore rather than a room word, so that "go to the bedroom" resolves to the
            // room and "go to bed" / "go to sleep" resolve to the spot she sleeps at.
            new[] { "sleep", "sleeping", "sleepy", "nap", "siesta", "dormir", "duerme",
                    "durmiendo", "acostarse", "acostar", "tumbarse", "descansar", "rest" },

            // Pastimes the routines name spots after ("SusanPraying", "ListenMusicRoom",
            // "StacyPhoneBedRoom", "StacySmokingOutside", "SusanLookingKitchen"...).
            new[] { "pray", "praying", "prayer", "reza", "rezar", "oracion", "orar" },
            new[] { "look", "looking", "look around", "mirar", "mira", "observar", "vistazo" },
            new[] { "music", "listen", "listening", "song", "songs", "radio", "musica", "escuchar",
                    "cancion", "canciones" },
            new[] { "phone", "call", "calling", "text", "texting", "llamar", "telefono", "celular", "movil" },
            new[] { "smoke", "smoking", "cigarette", "fumar", "fuma", "cigarro", "tabaco" },
            new[] { "masturbate", "masturbating", "masturbation", "masturbarse", "masturbar" },

            new[] { "read", "reading", "book", "leer", "leyendo", "libro", "revista", "magazine" },
            new[] { "dance", "dancing", "bailar", "baile" },
            new[] { "drink", "drinking", "drunk", "bar", "wine", "coffee", "beber", "bebiendo", "copa",
                    "vino", "cafe", "trago", "borracho" }
        };

        /// <summary>
        /// Both vocabularies, rooms first: ExpandQuery() expands a word through the first group that
        /// contains it, so the room reading always wins over the chore one.
        /// </summary>
        private static readonly string[][] AllSynonyms = BuildAllSynonyms();

        private static string[][] BuildAllSynonyms()
        {
            var all = new List<string[]>(AreaSynonyms.Length + ChoreSynonyms.Length);
            all.AddRange(AreaSynonyms);
            all.AddRange(ChoreSynonyms);
            return all.ToArray();
        }

        // ─────────────────────────────────────────────────────────────
        //  Normalization

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Split camelCase / PascalCase first: asset names are often "Kitchen_Cook" or "SofaSit".
            string spaced = Regex.Replace(text, @"(?<=[a-z0-9])(?=[A-Z])", " ");
            string lower = spaced.ToLowerInvariant();

            // Strip accents so "cocina"/"salón"/"baño" match their ASCII forms.
            lower = RemoveDiacritics(lower);

            // Underscores, digits and punctuation become separators ("Sit_Sofa_02" -> "sit sofa").
            var sb = new StringBuilder(lower.Length);
            foreach (char c in lower)
            {
                bool keep = (c >= 'a' && c <= 'z') || c == ' ' || c == '&';
                sb.Append(keep ? c : ' ');
            }

            string collapsed = Regex.Replace(sb.ToString(), @"\s+", " ");
            return collapsed.Trim();
        }

        /// <summary>
        /// Readable label for a place. Chore assets are named like "WashingPlates" or "Sit_Sofa";
        /// spacing them out gives the AI (and the player) something natural to copy, while the raw
        /// asset name stays in the spot's keywords so both spellings keep matching.
        /// </summary>
        public static string PrettyLabel(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "";

            string spaced = Regex.Replace(rawName, @"[_-]+", " ");
            spaced = Regex.Replace(spaced, @"(?<=[a-z0-9])(?=[A-Z])", " ");
            return Regex.Replace(spaced, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Readable label for a routine spot: the routine's description when it has one, otherwise the
        /// space-out asset name. Asset names are prefixed with their owner ("SusanLookingKitchen",
        /// "StacyPhoneBedRoom") and an NPC only ever lists her own spots, so that prefix is dropped.
        /// </summary>
        public static string PrettyLabel(string description, string assetName, string owner)
        {
            if (!string.IsNullOrEmpty(description)) return PrettyLabel(description);

            string label = PrettyLabel(assetName);
            string prefix = owner + " ";
            if (!string.IsNullOrEmpty(owner) && label.Length > prefix.Length &&
                label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                label = label.Substring(prefix.Length);
            }
            return label;
        }

        /// <summary>Lowercases and removes diacritics without touching the rest of the text.</summary>
        private static string RemoveDiacritics(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case 'á': case 'à': case 'â': case 'ä': case 'ã': case 'å': sb.Append('a'); break;
                    case 'é': case 'è': case 'ê': case 'ë': sb.Append('e'); break;
                    case 'í': case 'ì': case 'î': case 'ï': sb.Append('i'); break;
                    case 'ó': case 'ò': case 'ô': case 'ö': case 'õ': sb.Append('o'); break;
                    case 'ú': case 'ù': case 'û': case 'ü': sb.Append('u'); break;
                    case 'ñ': sb.Append('n'); break;
                    case 'ç': sb.Append('c'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────
        //  Reading a request

        /// <summary>
        /// True when the message looks like an order to go somewhere or do a chore somewhere
        /// ("go to the kitchen", "sweep the hallway", "do the laundry", "limpia el baño"). Used to
        /// add explicit guidance (and the list of known places) to the prompt for that turn.
        /// </summary>
        public static bool IsGoToRequest(string message)
        {
            string text = Normalize(message);
            if (text.Length == 0) return false;

            foreach (string phrase in MovePhrases)
            {
                if (ContainsWord(text, phrase)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the player is asking which places are available ("where can you go",
        /// "a donde puedes ir", "list places"), so the AI can answer with the known ones.
        /// </summary>
        public static bool IsPlacesQuestion(string message)
        {
            string text = Normalize(message);
            if (text.Length == 0) return false;

            return ContainsWord(text, "where can you go") || ContainsWord(text, "where can we go") ||
                   ContainsWord(text, "where do you go") || ContainsWord(text, "which places") ||
                   ContainsWord(text, "what places") || ContainsWord(text, "list places") ||
                   ContainsWord(text, "available places") || ContainsWord(text, "known places") ||
                   ContainsWord(text, "a donde puedes ir") || ContainsWord(text, "a donde vas") ||
                   ContainsWord(text, "a donde podemos ir") || ContainsWord(text, "donde puedes ir") ||
                   ContainsWord(text, "que lugares") || ContainsWord(text, "que sitios") ||
                   ContainsWord(text, "donde vamos") || ContainsWord(text, "donde esta") ||
                   ContainsWord(text, "where is the");
        }

        /// <summary>
        /// Extracts the destination words from a sentence: "go to the kitchen please" -> "kitchen",
        /// "lie down on the sofa" -> "sofa", "vete a la cocina" -> "cocina". Returns "" when nothing
        /// usable is left. A bare place ("kitchen") is returned as-is.
        /// </summary>
        public static string ExtractPlacePhrase(string message)
        {
            string text = Normalize(message);
            if (text.Length == 0) return "";

            string tail = text;
            foreach (string phrase in MovePhrases)
            {
                int idx = IndexOfWord(text, phrase);
                if (idx < 0) continue;

                tail = text.Substring(idx + phrase.Length);
                break;
            }

            var kept = new List<string>();
            foreach (string word in tail.Split(' '))
            {
                if (word.Length == 0) continue;
                if (StopWords.Contains(word)) continue;
                if (word.Length == 1) continue;
                kept.Add(word);
                if (kept.Count == 3) break;
            }

            // No verb matched: the whole message (minus stop words) is the place.
            if (kept.Count == 0 && tail == text)
            {
                foreach (string word in text.Split(' '))
                {
                    if (word.Length == 0 || StopWords.Contains(word) || word.Length == 1) continue;
                    kept.Add(word);
                    if (kept.Count == 3) break;
                }
            }

            return string.Join(" ", kept.ToArray());
        }

        // ─────────────────────────────────────────────────────────────
        //  Tokens

        /// <summary>
        /// Words of a request, with no synonym expansion: the words themselves plus their simple
        /// plural/singular variants. Stop words are dropped so that "the" cannot act as a match.
        /// </summary>
        public static List<string> DirectTokens(string text)
        {
            return BuildTokens(text, null, WordKind.Any, addPhrase: true);
        }

        /// <summary>
        /// The "where" of a request: the words of the request that name a place, expanded through
        /// <see cref="AreaSynonyms"/>. Words that are not a place — chores, and loose words like
        /// "room" — are left out, so the room decides which spot is meant and the chore only breaks
        /// ties ("dust the dining room" has to pick a dining-room spot even though a dusting spot
        /// exists, and "water the plant in the living room" must not lose to a spot that merely has
        /// the word "room" in its name).
        /// </summary>
        public static List<string> AreaTokens(string text)
        {
            return BuildTokens(text, AreaSynonyms, WordKind.Area, addPhrase: false);
        }

        /// <summary>The "what" of a request: its chore words, expanded through <see cref="ChoreSynonyms"/>.</summary>
        public static List<string> ChoreTokens(string text)
        {
            return BuildTokens(text, ChoreSynonyms, WordKind.Chore, addPhrase: false);
        }

        /// <summary>
        /// Expands a destination into the tokens used for matching: the words themselves, their
        /// simple plural variants, every word of each synonym group they belong to, and the whole
        /// phrase so that a multi-word name can match verbatim.
        /// </summary>
        public static List<string> ExpandQuery(string placePhrase)
        {
            return BuildTokens(placePhrase, AllSynonyms, WordKind.Any, addPhrase: true);
        }

        /// <summary>Which words of a request a token set is built from.</summary>
        private enum WordKind
        {
            /// <summary>Every word (the general reading).</summary>
            Any,
            /// <summary>Only words naming a place (the "where").</summary>
            Area,
            /// <summary>Only words naming a chore (the "what").</summary>
            Chore
        }

        /// <summary>
        /// Token builder shared by the three readings: keeps the words selected by
        /// <paramref name="kind"/> and expands each of them through the first group that contains
        /// it. <paramref name="addPhrase"/> adds the whole request as one token, so a candidate
        /// containing the request verbatim scores bonus points.
        /// </summary>
        private static List<string> BuildTokens(string text, string[][] groups, WordKind kind, bool addPhrase)
        {
            var tokens = new List<string>();
            var seen = new HashSet<string>();

            foreach (string word in Normalize(text).Split(' '))
            {
                if (kind == WordKind.Area && !IsAreaWord(word)) continue;
                if (kind == WordKind.Chore && !IsChoreWord(word)) continue;

                AddToken(tokens, seen, word);
                AddToken(tokens, seen, Plural(word));
                AddToken(tokens, seen, Singular(word));

                if (groups == null) continue;

                foreach (string[] group in groups)
                {
                    if (!GroupContains(group, word)) continue;
                    foreach (string synonym in group) AddToken(tokens, seen, synonym);
                    break;
                }
            }

            // Multi-word phrase, matched as a whole by Score().
            string normalized = Normalize(text);
            if (addPhrase && normalized.IndexOf(' ') > 0) AddToken(tokens, seen, normalized);

            return tokens;
        }

        private static void AddToken(List<string> tokens, HashSet<string> seen, string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 2) return;
            if (StopWords.Contains(token)) return;
            if (seen.Add(token)) tokens.Add(token);
        }

        // ─────────────────────────────────────────────────────────────
        //  Scoring

        /// <summary>
        /// Scores a candidate spot name/description against the expanded query tokens.
        /// Whole-word matches count double, substring matches count once; multi-word query
        /// phrases found verbatim add a bonus. 0 means "no relation".
        /// </summary>
        public static int Score(IEnumerable<string> queryTokens, string candidateText)
        {
            string candidate = Normalize(candidateText);
            if (candidate.Length == 0) return 0;

            int score = 0;
            foreach (string token in queryTokens)
            {
                if (string.IsNullOrEmpty(token)) continue;

                if (token.IndexOf(' ') >= 0)
                {
                    if (candidate.Contains(token)) score += 4;
                    continue;
                }

                if (token.Length < 3) continue;

                if (ContainsWord(candidate, token)) score += 3;
                else if (candidate.Contains(token)) score += 1;
            }

            return score;
        }

        /// <summary>
        /// Scoring used by the resolution step: a request is read twice — as a whole (the AI copies
        /// one of the place labels it was given, so a label has to match as-is) and as the
        /// destination extracted from it ("go to the kitchen" -> "kitchen") — and the better
        /// reading wins.
        /// </summary>
        public static int ScoreRequest(string query, string candidateText)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(candidateText)) return 0;

            int best = ScoreReading(query, candidateText);

            string phrase = ExtractPlacePhrase(query);
            if (!string.IsNullOrEmpty(phrase) && phrase != Normalize(query))
                best = Math.Max(best, ScoreReading(phrase, candidateText));

            return best;
        }

        /// <summary>
        /// Scores one reading of a request against a candidate: naming the room weighs three times
        /// as much as anything else, the chore it is asked to do adds its own points, and a spot
        /// matching both the room and the chore — what the player actually asked for — gets a bonus.
        /// </summary>
        private static int ScoreReading(string text, string candidateText)
        {
            int area = Score(AreaTokens(text), candidateText);
            int chore = Score(ChoreTokens(text), candidateText);
            int any = Score(ExpandQuery(text), candidateText);

            int score = area * 3 + chore + any;
            if (area > 0 && chore > 0) score += 10;
            return score;
        }

        /// <summary>Whole-word containment test on already-normalized text.</summary>
        public static bool ContainsWord(string normalizedText, string phrase)
        {
            if (string.IsNullOrEmpty(normalizedText) || string.IsNullOrEmpty(phrase)) return false;
            return (" " + normalizedText + " ").Contains(" " + phrase + " ");
        }

        /// <summary>Index of a whole-word phrase inside normalized text, or -1.</summary>
        private static int IndexOfWord(string normalizedText, string phrase)
        {
            string padded = " " + normalizedText + " ";
            int at = padded.IndexOf(" " + phrase + " ", StringComparison.Ordinal);
            return at < 0 ? -1 : at;
        }

        private static bool GroupContains(string[] group, string word)
        {
            foreach (string item in group)
            {
                if (item == word) return true;
            }
            return false;
        }

        /// <summary>True when the word names a chore (it appears in one of the chore groups).</summary>
        private static bool IsChoreWord(string word)
        {
            return IsInGroups(ChoreSynonyms, word);
        }

        /// <summary>True when the word names a place (it appears in one of the area groups).</summary>
        private static bool IsAreaWord(string word)
        {
            return IsInGroups(AreaSynonyms, word);
        }

        private static bool IsInGroups(string[][] groups, string word)
        {
            foreach (string[] group in groups)
            {
                if (GroupContains(group, word)) return true;
            }
            return false;
        }

        private static string Plural(string word)
        {
            if (word.Length < 3) return word;
            if (word.EndsWith("s")) return word;
            return word + "s";
        }

        private static string Singular(string word)
        {
            if (word.Length < 4) return word;
            if (word.EndsWith("es")) return word.Substring(0, word.Length - 2);
            if (word.EndsWith("s")) return word.Substring(0, word.Length - 1);
            return word;
        }
    }
}
