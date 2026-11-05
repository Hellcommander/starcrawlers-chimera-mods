using System;
using System.Collections.Generic;
using System.Linq;

namespace Breachwork.Titles
{
    /// <summary>One class's identity words for <see cref="Wordsmith"/>, most telling first.</summary>
    public sealed class WordsmithClass
    {
        public string id, name;
        /// <summary>Single adjectives for what the class does: "Blinking", "Lightless", "Spored".</summary>
        public List<string> traits = new List<string>();
        /// <summary>Stems that fuse onto other words: "Spore", "Rune", "Comet".</summary>
        public List<string> roots = new List<string>();
        /// <summary>The word a title ends on: "Seer", "Guard". A leading '-' fuses it onto a root: "-smith" makes "Runesmith".</summary>
        public List<string> heads = new List<string>();
        /// <summary>What the class does, strongest first (fire, mind, swarm, ...). Words of two classes with the same first theme may fuse ("Psi-Spored").</summary>
        public List<string> themes = new List<string>();
    }

    /// <summary>A forged title and which classes each of its words stands for.</summary>
    public sealed class WordsmithTitle
    {
        public string text;
        /// <summary>How many of the classes the title names.</summary>
        public int covered;
        /// <summary>Each word part ("Rune", "-smith", "Blinking") with the names of the classes it came from.</summary>
        public List<KeyValuePair<string, List<string>>> parts = new List<KeyValuePair<string, List<string>>>();
    }

    /// <summary>
    /// The wordsmith: fuses the classes' identity words into one short title that names as many of the classes as it
    /// can and still reads as a name ("Blinking Spore-Runesmith"). A word several classes list stands for all of them,
    /// so classes that share an identity merge into one word. A title is up to two modifiers (a trait, a root fused onto
    /// a trait like "Psi-Spored", or a root pair like "Fire-Frost") before a head (a head word, or a root fused onto
    /// one: "Runesmith", "Hive-Guard"). Scoring: every class named counts most; then each class's first (most telling)
    /// words, plain traits over hyphens, a root fused onto a head of its own class over one from another class, and
    /// fewer letters. At most two hyphens and one root pair, so it stays readable. It is a fixed function of the class
    /// set (order never matters). No Unity here: the Class Titler compiles this same file and shows the same titles.
    /// </summary>
    public static class Wordsmith
    {
        private sealed class Word
        {
            public string text;
            public int mask, primary;
            public bool suffix;
        }

        /// <summary>Per class (bit), the classes it shares a theme with, itself included.</summary>
        private static int[] links;

        /// <summary>Two classes whose main (first) themes are the same: what they mostly do is alike.</summary>
        private static bool SameMain(WordsmithClass a, WordsmithClass b)
        {
            string x = a.themes?.FirstOrDefault(), y = b.themes?.FirstOrDefault();
            return !string.IsNullOrWhiteSpace(x) && string.Equals(x.Trim(), (y ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Two words may fuse when they come from one class, or from two classes that share a theme.</summary>
        private static bool Akin(Word a, Word b)
        {
            for (int i = 0; i < links.Length; i++)
                if ((a.mask & (1 << i)) != 0 && (b.mask & links[i]) != 0) return true;
            return false;
        }

        private sealed class Part
        {
            public string text;
            public int mask, primary, hyphens, pairs;
            public bool hyphened;
            public double quality;
            public HashSet<string> stems;
            public List<Word> words;
        }

        private sealed class State
        {
            public Part head;
            public List<Part> mods = new List<Part>();
            public int mask, primary, len, hyphens, pairs;
            public double quality;
            public HashSet<string> stems;
        }

        private static string Norm(string w)
        {
            w = (w ?? "").Trim();
            if (w.Length == 0) return "";
            bool suffix = w[0] == '-';
            string body = w.TrimStart('-').Trim();
            if (body.Length == 0) return "";
            return suffix ? "-" + body.ToLowerInvariant() : char.ToUpperInvariant(body[0]) + body.Substring(1);
        }

        private static List<Word> Collect(IList<WordsmithClass> classes, Func<WordsmithClass, List<string>> pick)
        {
            var map = new Dictionary<string, Word>(StringComparer.OrdinalIgnoreCase);
            var order = new List<Word>();
            for (int i = 0; i < classes.Count; i++)
            {
                var list = pick(classes[i]) ?? new List<string>();
                for (int k = 0; k < list.Count; k++)
                {
                    string w = Norm(list[k]);
                    if (w == "") continue;
                    if (!map.TryGetValue(w, out Word word)) { word = new Word { text = w, suffix = w[0] == '-' }; map[w] = word; order.Add(word); }
                    word.mask |= 1 << i;
                    if (k == 0) word.primary |= 1 << i;
                }
            }
            return order;
        }

        private static HashSet<string> Stems(string text) =>
            new HashSet<string>(text.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.ToLowerInvariant()));

        private static int Bits(int m) { int n = 0; for (; m != 0; m &= m - 1) n++; return n; }

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") { h ^= c; h *= 16777619; }
            return h;
        }

        /// <summary>Two word sets that share a stem ("Spore" and "Spored", "Rune" and "Runes") would repeat themselves.</summary>
        private static bool Overlaps(HashSet<string> a, HashSet<string> b)
        {
            foreach (string x in a)
                foreach (string y in b)
                {
                    // y and i sound alike: "Psi" and "Psyker" would say the same thing twice
                    string s = x.Replace('y', 'i'), t = y.Replace('y', 'i');
                    if (s == t || (s.Length >= 3 && t.StartsWith(s, StringComparison.Ordinal)) || (t.Length >= 3 && s.StartsWith(t, StringComparison.Ordinal))) return true;
                }
            return false;
        }

        private static Part Make(string text, int pairs, double quality, params Word[] words)
        {
            var p = new Part { text = text, pairs = pairs, quality = quality, words = words.ToList() };
            foreach (Word w in words) { p.mask |= w.mask; p.primary |= w.primary; }
            p.hyphens = text.Count(c => c == '-');
            p.hyphened = p.hyphens > 0;
            p.stems = new HashSet<string>(words.SelectMany(w => Stems(w.text.TrimStart('-'))));
            return p;
        }

        private const double PerClass = 10, PerPrimary = 2, PerLetter = 0.25;

        private static double Score(State s) => PerClass * Bits(s.mask) + PerPrimary * Bits(s.primary) + s.quality - PerLetter * s.len;

        private static State With(State s, Part m)
        {
            var n = new State
            {
                head = s.head, mask = s.mask | m.mask, primary = s.primary | m.primary, len = s.len + 1 + m.text.Length,
                hyphens = s.hyphens + m.hyphens, pairs = s.pairs + m.pairs, quality = s.quality + m.quality,
                stems = new HashSet<string>(s.stems),
            };
            n.mods.AddRange(s.mods);
            n.mods.Add(m);
            n.stems.UnionWith(m.stems);
            return n;
        }

        /// <summary>
        /// Up to <paramref name="count"/> titles for these classes, best first. None longer than
        /// <paramref name="maxLength"/> characters unless no head fits.
        /// </summary>
        public static List<WordsmithTitle> Titles(IEnumerable<WordsmithClass> input, int maxLength = 32, int count = 8)
        {
            var classes = (input ?? Enumerable.Empty<WordsmithClass>()).Where(c => c != null && !string.IsNullOrEmpty(c.id))
                .GroupBy(c => c.id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(c => c.id, StringComparer.OrdinalIgnoreCase).Take(30).ToList();
            var result = new List<WordsmithTitle>();
            if (classes.Count == 0) return result;
            string sig = string.Join("+", classes.Select(c => c.id));
            links = new int[classes.Count];
            for (int i = 0; i < classes.Count; i++)
                for (int j = 0; j < classes.Count; j++)
                    if (i == j || SameMain(classes[i], classes[j]))
                        links[i] |= 1 << j;
            List<Word> traits = Collect(classes, c => c.traits);
            List<Word> roots = Collect(classes, c => c.roots).Where(w => !w.suffix).ToList();
            List<Word> heads = Collect(classes, c => c.heads);

            // heads: a head word alone, or a root fused onto it (better when both come from one class)
            var headParts = new List<Part>();
            foreach (Word h in heads)
            {
                if (!h.suffix) headParts.Add(Make(h.text, 0, 0, h));
                foreach (Word r in roots)
                {
                    if (Overlaps(Stems(r.text), Stems(h.text.TrimStart('-')))) continue;
                    bool kin = (r.mask & h.mask) != 0, akin = kin || Akin(r, h);
                    // a fused head reads as one word, so its root must be its own class's ("Runesmith", never "Chaossmith")
                    if (h.suffix) { if (kin) headParts.Add(Make(r.text + h.text.Substring(1), 0, 0, r, h)); }
                    else if (akin) headParts.Add(Make(r.text + "-" + h.text, 0, kin ? -1 : -2, r, h));
                }
            }
            // a class set without head words still gets a title: a root names a "-born"
            if (headParts.Count == 0)
                foreach (Word r in roots) headParts.Add(Make(r.text + "born", 0, -2, r));
            if (headParts.Count == 0) return result;
            if (headParts.Any(h => h.text.Length <= maxLength)) headParts.RemoveAll(h => h.text.Length > maxLength);

            // modifiers: a trait, a root fused onto a trait, or two roots
            var mods = new List<Part>();
            // traits written with a leading '-' ("-spored") only come fused onto a root ("Psi-Spored"); the rest stand alone
            foreach (Word t in traits.Where(w => !w.suffix)) mods.Add(Make(t.text, 0, 0, t));
            foreach (Word r in roots)
                foreach (Word t in traits.Where(w => w.suffix))
                {
                    string body = t.text.Substring(1);
                    if (Akin(r, t) && !Overlaps(Stems(r.text), Stems(body))) mods.Add(Make(r.text + "-" + char.ToUpperInvariant(body[0]) + body.Substring(1), 0, (r.mask & t.mask) != 0 ? 0 : -1, r, t));
                }
            for (int a = 0; a < roots.Count; a++)
                for (int b = a + 1; b < roots.Count; b++)
                    if (Akin(roots[a], roots[b]) && !Overlaps(Stems(roots[a].text), Stems(roots[b].text))) mods.Add(Make(roots[a].text + "-" + roots[b].text, 1, -4, roots[a], roots[b]));

            // a small beam per head: the best few one-modifier titles, each tried with a second modifier
            const int Beam = 6;
            var done = new List<State>();
            foreach (Part head in headParts)
            {
                var start = new State { head = head, mask = head.mask, primary = head.primary, len = head.text.Length, hyphens = head.hyphens, quality = head.quality, stems = new HashSet<string>(head.stems) };
                done.Add(start);
                var level = new List<State> { start };
                for (int step = 0; step < 2; step++)
                {
                    var next = new List<State>();
                    foreach (State s in level)
                        foreach (Part m in mods)
                        {
                            if ((m.mask & ~s.mask) == 0 || s.len + 1 + m.text.Length > maxLength) continue;
                            if (s.hyphens + m.hyphens > 2 || s.pairs + m.pairs > 1 || Overlaps(s.stems, m.stems)) continue;
                            next.Add(With(s, m));
                        }
                    level = next.OrderByDescending(Score).Take(Beam).ToList();
                    done.AddRange(level);
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (State s in done.OrderByDescending(Score).ThenBy(x => Hash(sig + "|" + Text(x))))
            {
                string text = Text(s);
                if (!seen.Add(text)) continue;
                var title = new WordsmithTitle { text = text, covered = Bits(s.mask) };
                foreach (Part p in Ordered(s).Concat(new[] { s.head }))
                    foreach (Word w in p.words)
                        title.parts.Add(new KeyValuePair<string, List<string>>(w.text,
                            Enumerable.Range(0, classes.Count).Where(i => (w.mask & (1 << i)) != 0).Select(i => classes[i].name ?? classes[i].id).ToList()));
                result.Add(title);
                if (result.Count >= Math.Max(1, count)) break;
            }
            return result;
        }

        /// <summary>
        /// Which classes a title names through their identity words, for titles written by hand or by Ollama: a word
        /// of the title is one of a class's traits, roots or heads, or starts with a root ("Sporecaller"), or ends
        /// with a head ("Runesmith"), or is a form of one ("Spored" for "Spore"). Indices into <paramref name="classes"/>.
        /// </summary>
        public static List<int> Names(string title, IList<WordsmithClass> classes)
        {
            var found = new List<int>();
            if (string.IsNullOrWhiteSpace(title) || classes == null) return found;
            var tokens = title.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(t => new string(t.Where(char.IsLetter).ToArray()).ToLowerInvariant().Replace('y', 'i')).Where(t => t.Length >= 2).ToList();
            for (int i = 0; i < classes.Count; i++)
            {
                var c = classes[i];
                if (c == null) continue;
                var pieces = (c.traits ?? new List<string>()).Concat(c.roots ?? new List<string>()).Concat(c.heads ?? new List<string>())
                    .Select(p => (p ?? "").Trim().TrimStart('-').ToLowerInvariant().Replace('y', 'i')).Where(p => p.Length >= 3).Distinct().ToList();
                bool hit = false;
                foreach (string t in tokens)
                {
                    foreach (string p in pieces)
                        if (t == p || t.StartsWith(p, StringComparison.Ordinal) || (p.Length >= 4 && t.EndsWith(p, StringComparison.Ordinal))
                            || (t.Length >= 4 && p.StartsWith(t, StringComparison.Ordinal))) { hit = true; break; }
                    if (hit) break;
                }
                if (hit) found.Add(i);
            }
            return found;
        }

        /// <summary>Plain traits first, then the hyphened modifiers: "Brawny Fire-Frost Splice-Hive".</summary>
        private static IEnumerable<Part> Ordered(State s) => s.mods.OrderBy(p => p.hyphened ? 1 : 0);

        private static string Text(State s) => string.Join(" ", Ordered(s).Select(p => p.text).Concat(new[] { s.head.text }));
    }
}
