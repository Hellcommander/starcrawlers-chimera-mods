// An example to start your own portrait tool from: use, copy and change it for any purpose, with no conditions and no credit needed.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PortraitMaker
{
    /// <summary>
    /// Draws a character portrait from code, with no AI, GPU or install. The game's Character Portrait Tools mod starts this
    /// with the same job file every portrait tool gets: { "name", "level", "appearance", "output", "classes", "abilities", "gear" },
    /// and optional "effects" and "form". The picture appears at "output" only once it is whole; a job.log holds why it failed,
    /// and a non-zero exit means no picture. Each run rolls a new face, and the job decides the rest: classes set the room's
    /// light, each item's own icon and colours what's worn and held, appearance words the looks, mutations the body.
    /// </summary>
    internal static class Program
    {
        private const int Size = 768;

        private static int Main(string[] args)
        {
            if (args.Length == 0) return 2;
            string log = Path.ChangeExtension(args[0], ".log");
            try
            {
                var job = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(args[0], Encoding.UTF8));
                string output = Text(job, "output");
                string partial = Path.ChangeExtension(args[0], ".partial");
                using (Bitmap picture = new Painter(new Character(job, output)).Paint())
                    picture.Save(partial, ImageFormat.Png);
                if (File.Exists(output)) File.Delete(output);
                File.Move(partial, output);
                File.WriteAllText(log, "done: " + output + "\n");
                return 0;
            }
            catch (Exception e)
            {
                File.WriteAllText(log, e + "\n" + e.GetType().Name + ": " + e.Message + "\n"); // the game shows the last line
                return 1;
            }
        }

        internal static string Text(Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out object v) ? v as string ?? "" : "";

        internal static IEnumerable<Dictionary<string, object>> List(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object v) && v is IEnumerable e ? e.OfType<Dictionary<string, object>>() : new Dictionary<string, object>[0];

        internal static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) h = (h ^ c) * 16777619;
            return h;
        }
    }

    internal enum Held { None, Blade, Gun, Staff }

    internal enum Headwear { None, Glasses, Visor, Hat, Helmet }

    /// <summary>One item from the job: its name, its icon file and its own colours.</summary>
    internal class Piece
    {
        public string name, about, icon;
        public Color main, trim;

        public Piece(Dictionary<string, object> gear, Color fallback, Color fallbackTrim)
        {
            name = Regex.Replace(Program.Text(gear, "name"), "<[^>]+>", "");
            about = name + " " + Regex.Replace(Program.Text(gear, "description"), "<[^>]+>", "");
            icon = Program.Text(gear, "icon");
            var colors = gear.TryGetValue("colors", out object c) && c is IEnumerable e ? e.OfType<string>().Select(ColorTranslator.FromHtml).ToList() : new List<Color>();
            main = colors.Count > 0 ? colors[0] : fallback;
            trim = colors.Count > 1 ? colors[1] : Painter.Mix(main, fallbackTrim, 0.6f);
        }
    }

    /// <summary>What the job says about the character, boiled down to what the picture can show.</summary>
    internal class Character
    {
        /// <summary>Words in a class that say what colour it is. Anything else gets a colour from its name.</summary>
        private static readonly (string words, Color color)[] Elements =
        {
            ("fire|flame|ember|cinder|burn|pyro|lava|forge|smith", Color.FromArgb(255, 122, 44)),
            ("frost|ice|cryo|cold|glacier|snow", Color.FromArgb(120, 210, 255)),
            ("water|tide|hydro|brine|storm|squall", Color.FromArgb(40, 150, 230)),
            ("lightning|shock|volt|arc|tesla|flux", Color.FromArgb(120, 150, 255)),
            ("poison|toxin|venom|acid|plague|mutat|bio", Color.FromArgb(120, 225, 70)),
            ("blood|gore|flesh|crimson|berserk|butcher", Color.FromArgb(215, 35, 50)),
            ("psi|mind|void|astral|shadow|dark|necro|soul", Color.FromArgb(175, 90, 245)),
            ("holy|sun|light|radian|medic|heal", Color.FromArgb(255, 220, 110)),
            ("nano|cyber|tech|hack|drone|engineer|robot", Color.FromArgb(50, 230, 200)),
        };

        private static readonly Color[] Skins =
        {
            Color.FromArgb(246, 214, 188), Color.FromArgb(232, 184, 148), Color.FromArgb(198, 142, 102),
            Color.FromArgb(150, 100, 70), Color.FromArgb(104, 68, 50),
        };

        private static readonly (string words, Color color)[] Hairs =
        {
            ("blonde?|golden", Color.FromArgb(224, 188, 110)), ("red|ginger|auburn", Color.FromArgb(166, 66, 34)),
            ("grey|gray|silver|white|old", Color.FromArgb(178, 182, 190)), ("brown", Color.FromArgb(86, 56, 36)),
            ("black|dark", Color.FromArgb(28, 26, 32)), ("blue", Color.FromArgb(50, 100, 210)), ("green", Color.FromArgb(50, 160, 90)),
            ("pink", Color.FromArgb(230, 100, 170)), ("purple|violet", Color.FromArgb(130, 70, 190)),
        };

        public readonly Random roll;
        public readonly Color accent, second, skin, hair, eyes;
        public readonly bool feminine, bald, longHair, beard, scar, armoured, spiked, blotched, extraEye, tentacles, formed, aura, hurt;
        public readonly Held held, stowed;
        public readonly Headwear headwear;
        public readonly Piece head, torso, neck, hands, mainHand, offHand, onBack;
        public readonly Color glow;

        private static bool Says(string text, string words) => Regex.IsMatch(text, @"\b(" + words + ")", RegexOptions.IgnoreCase);

        private static Color ClassColor(string text, string name)
        {
            foreach (var (words, color) in Elements)
                if (Says(text, words)) return color;
            float hue = Program.Hash(name) % 360;
            return Painter.Hsv(hue, 0.65f, 0.95f);
        }

        private static Held Kind(string name)
        {
            if (Says(name, "gun|rifle|pistol|cannon|blaster|carbine|shotgun|launcher")) return Held.Gun;
            if (Says(name, "staff|rod|wand|spear|pike|lance|sceptre|scepter")) return Held.Staff;
            return Held.Blade;
        }

        public Character(Dictionary<string, object> job, string output)
        {
            roll = new Random((int)Program.Hash(Path.GetFileName(output) + Program.Text(job, "name")));
            var classes = Program.List(job, "classes").ToList();
            string Named(int i) => i < classes.Count ? Program.Text(classes[i], "name") : "Crawler";
            string About(int i) => i < classes.Count ? Named(i) + " " + Program.Text(classes[i], "description") : "";
            accent = ClassColor(About(0), Named(0));
            second = classes.Count > 1 ? ClassColor(About(1), Named(1)) : Painter.Hsv(accent.GetHue() + 150f, 0.5f, 0.8f);

            string look = Program.Text(job, "appearance");
            feminine = Says(look, "wom[ae]n|female|girl|she\\b|her\\b") || (!Says(look, "man\\b|men\\b|male|boy|he\\b|his\\b") && roll.Next(2) == 0);
            bald = Says(look, "bald|shaved|hairless") || (look.Length == 0 && roll.Next(7) == 0);
            longHair = Says(look, "long hair|braid|ponytail|flowing") || (!Says(look, "short") && roll.Next(feminine ? 3 : 7) != 0 == feminine);
            beard = Says(look, "beard") || (!feminine && look.Length == 0 && roll.Next(3) == 0);
            skin = Says(look, "pale|fair") ? Skins[0] : Says(look, "dark[- ]skin|black skin|brown skin") ? Skins[4] : Skins[roll.Next(Skins.Length)];
            hair = Hairs.Where(h => Says(look, "(" + h.words + @")(-| )?(haired|hair)")).Select(h => h.color).DefaultIfEmpty(Color.Empty).First();
            if (hair.IsEmpty) hair = Hairs[roll.Next(Hairs.Length - 4)].color;
            eyes = Painter.Mix(accent, Color.FromArgb(120, 150, 170), 0.45f);

            scar = Says(look, "scar") || Convert.ToInt32(job.TryGetValue("level", out object level) ? level : 1) >= 10;
            foreach (Dictionary<string, object> g in Program.List(job, "gear"))
            {
                string slot = Program.Text(g, "slot"), name = Program.Text(g, "name");
                var piece = new Piece(g, second, accent);
                name = piece.about; // the description says what the name doesn't
                if (slot == "Head")
                {
                    head = piece;
                    headwear = Says(name, "glasses|goggles|specs|spectacles|shades|monocle|lens") ? Headwear.Glasses
                        : Says(name, "visor|eyepiece|hud") ? Headwear.Visor
                        : Says(name, "hat|cap|beret|fedora|hood|cowl|bandana|crown|tiara|wig|hair") ? Headwear.Hat : Headwear.Helmet;
                }
                else if (slot == "Torso")
                {
                    torso = piece;
                    armoured = !Says(name, "shirt|jacket|coat|robe|tunic|hoodie|suit|garb|sweater");
                    spiked = Says(name, "thorn|spike|barb|spine|bramble");
                }
                else if (slot == "Neck") neck = piece;
                else if (slot == "Hands") hands = piece;
                else if (slot == "stowed") { stowed = Kind(name); onBack = piece; }
                else if (slot == "main hand") { held = Kind(name); mainHand = piece; }
                else if (slot == "off hand") offHand = piece;
            }

            var effects = Program.List(job, "effects").ToList();
            var mutations = effects.Select(e => Regex.Replace(Program.Text(e, "name"), "<[^>]+>", "")).Where(n => Says(n, "mutation")).ToList();
            tentacles = mutations.Any(m => Says(m, "tentacle"));
            extraEye = mutations.Any(m => Says(m, "eye"));
            blotched = mutations.Any(m => !Says(m, "tentacle|eye"));
            if (effects.Any(e => Says(Program.Text(e, "name"), "poison skin|toxic skin"))) skin = Painter.Mix(skin, Color.FromArgb(110, 190, 80), 0.3f);
            hurt = effects.Any(e => e.TryGetValue("debuff", out object d) && d is bool b && b);
            aura = effects.Any(e => !(e.TryGetValue("debuff", out object d) && d is bool b && b));
            formed = job.TryGetValue("form", out object form) && form != null;
            string formText = formed ? Program.Text(form as Dictionary<string, object>, "name") + " " + Program.Text(form as Dictionary<string, object>, "description") : "";
            glow = formed ? ClassColor(formText, formText) : accent;
        }
    }

    /// <summary>Paints back to front: station, aura, stowed gear, body, held gear, head. All shapes are in 768 x 768.</summary>
    internal class Painter
    {
        private readonly Character c;
        private Graphics g;

        public Painter(Character character) => c = character;

        public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        private static Color Dark(Color a, float t) => Mix(a, Color.FromArgb(8, 10, 18), t);
        private static Color Light(Color a, float t) => Mix(a, Color.White, t);
        private static Color Fade(Color a, int alpha) => Color.FromArgb(alpha, a);

        public static Color Hsv(float h, float s, float v)
        {
            h = (h % 360f + 360f) % 360f;
            float k(float n) { float x = (n + h / 60f) % 6f; return v - v * s * Math.Max(0f, Math.Min(Math.Min(x, 4f - x), 1f)); }
            return Color.FromArgb((int)(k(5) * 255), (int)(k(3) * 255), (int)(k(1) * 255));
        }

        private float R(float from, float to) => from + (float)c.roll.NextDouble() * (to - from);

        public Bitmap Paint()
        {
            var bmp = new Bitmap(768, 768, PixelFormat.Format32bppArgb);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Station();
            if (c.aura || c.formed) Aura();
            if (c.onBack != null && !Item(c.onBack, 610f, 330f, 300f, 28f)) Weapon(c.stowed, 560f, 720f, 28f, 0.55f);
            if (c.tentacles) Tentacles();
            Body();
            if (c.mainHand != null && !Item(c.mainHand, 130f, 610f, 270f, -12f)) Weapon(c.held, 150f, 760f, -24f, 1f);
            if (c.offHand != null) Item(c.offHand, 640f, 620f, 240f, 12f);
            Head();
            if (c.formed) Motes();
            Finish();
            g.Dispose();
            return bmp;
        }

        /// <summary>A station corridor in perspective, lit from the class's colours.</summary>
        private void Station()
        {
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, 768, 768), Dark(c.second, 0.72f), Dark(c.accent, 0.9f), 75f))
                g.FillRectangle(b, 0, 0, 768, 768);
            var vanish = new PointF(R(300f, 470f), R(250f, 340f));
            using (var p = new Pen(Fade(Light(c.second, 0.2f), 42), 3f))
            {
                foreach (var e in new[] { new PointF(0, 0), new PointF(768, 0), new PointF(0, 768), new PointF(768, 768), new PointF(0, 380), new PointF(768, 380) })
                    g.DrawLine(p, vanish, e);
                for (float t = 0.18f; t < 0.95f; t *= 1.45f)
                    g.DrawRectangle(p, vanish.X * (1f - t), vanish.Y * (1f - t), 768f * t, 768f * t);
            }
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(vanish.X - 190f, vanish.Y - 190f, 380f, 380f);
                using (var b = new PathGradientBrush(path) { CenterColor = Fade(c.second, 120), SurroundColors = new[] { Fade(c.second, 0) } })
                    g.FillPath(b, path);
            }
            using (var b = new SolidBrush(Fade(Light(c.accent, 0.3f), 110)))
                for (int i = 0; i < 6; i++) g.FillRectangle(b, R(0f, 760f), R(40f, 420f), R(14f, 44f), 3f);
        }

        private void Glow(float x, float y, float radius, Color color, int alpha)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x - radius, y - radius, radius * 2f, radius * 2f);
                using (var b = new PathGradientBrush(path) { CenterColor = Fade(color, alpha), SurroundColors = new[] { Fade(color, 0) } })
                    g.FillPath(b, path);
            }
        }

        private void Aura()
        {
            Glow(384f, 390f, c.formed ? 430f : 340f, c.glow, c.formed ? 210 : 130);
            using (var p = new Pen(Fade(Light(c.glow, 0.4f), 80), 3f))
                for (int i = 0; i < 18; i++)
                {
                    double a = c.roll.NextDouble() * Math.PI * 2d;
                    float r = R(210f, 340f);
                    g.DrawLine(p, 384f + (float)Math.Cos(a) * r, 390f + (float)Math.Sin(a) * r, 384f + (float)Math.Cos(a) * (r + R(30f, 90f)), 390f + (float)Math.Sin(a) * (r + R(30f, 90f)));
                }
        }

        /// <summary>The item's real icon, on a halo of its colour so it reads against the room. False when the job has none.</summary>
        private bool Item(Piece p, float x, float y, float size, float tilt, bool halo = true)
        {
            if (string.IsNullOrEmpty(p.icon) || !File.Exists(p.icon)) return false;
            if (halo) Glow(x, y, size * 0.72f, p.main, 120);
            GraphicsState was = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(tilt);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var icon = Image.FromFile(p.icon)) g.DrawImage(icon, -size / 2f, -size / 2f, size, size);
            g.Restore(was);
            return true;
        }

        /// <summary>Thick tapering arms out of the shoulders, with suckers down their inner side.</summary>
        private void Tentacles()
        {
            Color flesh = Mix(c.skin, Color.FromArgb(120, 60, 130), 0.6f);
            foreach (int side in new[] { -1, 1 })
                for (int arm = 0; arm < 2; arm++)
                {
                    PointF a = new PointF(384f + side * 200f, 640f), b = new PointF(384f + side * (330f + arm * 30f), R(520f, 640f));
                    PointF d = new PointF(384f + side * (240f + arm * 90f), R(250f, 330f) + arm * 150f), e = new PointF(384f + side * R(330f, 370f), R(130f, 200f) + arm * 170f);
                    var spots = new List<(float x, float y, float r)>();
                    for (float t = 0f; t <= 1f; t += 0.012f)
                    {
                        float u = 1f - t;
                        spots.Add((u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * d.X + t * t * t * e.X,
                            u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * d.Y + t * t * t * e.Y, 44f * u + 7f));
                    }
                    using (var edge = new SolidBrush(Dark(flesh, 0.45f)))
                        foreach (var (x, y, r) in spots) g.FillEllipse(edge, x - r - 3f, y - r - 3f, (r + 3f) * 2f, (r + 3f) * 2f);
                    using (var body = new SolidBrush(flesh))
                        foreach (var (x, y, r) in spots) g.FillEllipse(body, x - r, y - r, r * 2f, r * 2f);
                    using (var sucker = new SolidBrush(Light(flesh, 0.45f)))
                        for (int i = 4; i < spots.Count; i += 8)
                            g.FillEllipse(sucker, spots[i].x - side * spots[i].r * 0.5f - spots[i].r * 0.3f, spots[i].y - spots[i].r * 0.3f, spots[i].r * 0.6f, spots[i].r * 0.6f);
                }
        }

        private void Weapon(Held kind, float x, float y, float tilt, float light)
        {
            GraphicsState was = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(tilt);
            Color metal = Dark(Color.FromArgb(150, 165, 185), 1f - light * 0.8f), edge = Mix(metal, c.accent, 0.7f);
            using (var body = new SolidBrush(metal))
            using (var dark = new SolidBrush(Dark(metal, 0.6f)))
            using (var bright = new SolidBrush(edge))
            {
                if (kind == Held.Blade)
                {
                    g.FillPolygon(body, new[] { new PointF(-22, -80), new PointF(-16, -520), new PointF(4, -590), new PointF(22, -520), new PointF(22, -80) });
                    g.FillPolygon(bright, new[] { new PointF(10, -80), new PointF(10, -530), new PointF(22, -520), new PointF(22, -80) });
                    g.FillRectangle(dark, -52, -86, 104, 20);
                }
                else if (kind == Held.Gun)
                {
                    g.FillRectangle(dark, -30, -470, 60, 400);
                    g.FillRectangle(body, -18, -560, 36, 110);
                    g.FillRectangle(bright, -36, -330, 72, 20);
                    g.FillRectangle(bright, 12, -450, 8, 360);
                }
                else
                {
                    g.FillRectangle(dark, -11, -520, 22, 520);
                    g.FillEllipse(bright, -36, -590, 72, 72);
                    Glow(0f, -554f, 110f, c.accent, (int)(170 * light));
                }
            }
            g.Restore(was);
        }

        private void Body()
        {
            float shoulder = c.feminine ? 196f : 240f, neck = c.feminine ? 42f : 56f;
            Color suit = c.torso != null ? Dark(c.torso.main, 0.35f) : Dark(c.second, 0.68f);
            using (var path = new GraphicsPath())
            {
                path.AddBezier(384f - neck, 510f, 384f - neck, 548f, 384f - shoulder * 0.7f, 548f, 384f - shoulder, 600f);
                path.AddLine(384f - shoulder, 600f, 384f - shoulder - 36f, 768f);
                path.AddLine(384f - shoulder - 36f, 768f, 384f + shoulder + 36f, 768f);
                path.AddLine(384f + shoulder + 36f, 768f, 384f + shoulder, 600f);
                path.AddBezier(384f + shoulder, 600f, 384f + shoulder * 0.7f, 548f, 384f + neck, 548f, 384f + neck, 510f);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new Rectangle(100, 520, 570, 250), Light(suit, 0.12f), Dark(suit, 0.55f), 25f))
                    g.FillPath(b, path);
            }
            using (var skin = new LinearGradientBrush(new Rectangle(330, 460, 110, 120), Dark(c.skin, 0.12f), Dark(c.skin, 0.5f), 90f))
                g.FillPolygon(skin, new[] { new PointF(384f - neck, 470f), new PointF(384f + neck, 470f), new PointF(384f + neck + 10f, 562f), new PointF(384f, 606f), new PointF(384f - neck - 10f, 562f) });

            if (c.torso != null && Item(c.torso, 384f, 668f, 560f, 0f, false)) { }
            else if (c.armoured) Armour(shoulder);
            else
                using (var p = new Pen(Fade(c.torso?.trim ?? c.accent, 190), 7f))
                {
                    g.DrawLine(p, 384f - neck - 10f, 566f, 384f, 640f);
                    g.DrawLine(p, 384f + neck + 10f, 566f, 384f, 640f);
                    g.DrawLine(p, 384f, 640f, 384f, 768f);
                }
            if (c.neck != null)
            {
                using (var p = new Pen(c.neck.main, 9f))
                    g.DrawArc(p, 384f - neck - 12f, 530f, (neck + 12f) * 2f, 44f, 10f, 160f);
                Item(c.neck, 384f, 650f, 92f, 0f);
            }
            if (c.hands != null && !c.tentacles && !Item(c.hands, 384f, 720f, 170f, 0f, false))
                using (var b = new SolidBrush(c.hands.main))
                {
                    g.FillEllipse(b, 384f - shoulder - 46f, 712f, 108f, 90f);
                    g.FillEllipse(b, 384f + shoulder - 62f, 712f, 108f, 90f);
                }
        }

        private void Armour(float shoulder)
        {
            Color plate = c.torso.main, trim = c.torso.trim;
            using (var b = new SolidBrush(plate))
            using (var dark = new SolidBrush(Dark(plate, 0.45f)))
            using (var p = new Pen(trim, 5f))
            {
                var chest = new[] { new PointF(284, 596), new PointF(484, 596), new PointF(510, 700), new PointF(384, 768), new PointF(258, 700) };
                g.FillPolygon(dark, chest);
                g.DrawPolygon(p, chest);
                g.FillPolygon(b, new[] { new PointF(384, 612), new PointF(476, 612), new PointF(494, 690), new PointF(384, 748) });
                foreach (int side in new[] { -1, 1 })
                {
                    var pad = new[]
                    {
                        new PointF(384f + side * (shoulder - 92f), 548f), new PointF(384f + side * (shoulder + 6f), 562f),
                        new PointF(384f + side * (shoulder + 42f), 650f), new PointF(384f + side * (shoulder - 62f), 668f),
                    };
                    g.FillPolygon(b, pad);
                    g.DrawPolygon(p, pad);
                    if (!c.spiked) continue;
                    for (int i = 0; i < 4; i++)
                    {
                        float x = pad[0].X + (pad[1].X - pad[0].X) * (i + 0.5f) / 4f, y = pad[0].Y + (pad[1].Y - pad[0].Y) * (i + 0.5f) / 4f;
                        g.FillPolygon(b, new[] { new PointF(x - 13f, y + 5f), new PointF(x + 13f, y + 5f), new PointF(x + side * 7f, y - 46f) });
                        g.DrawPolygon(p, new[] { new PointF(x - 13f, y + 5f), new PointF(x + side * 7f, y - 46f), new PointF(x + 13f, y + 5f) });
                    }
                }
            }
        }

        private void Head()
        {
            float w = c.feminine ? 118f : 130f, top = 148f, chin = c.feminine ? 496f : 510f, jaw = c.feminine ? 54f : 76f;
            var face = new GraphicsPath();
            face.AddBezier(384f - w, 300f, 384f - w, 180f, 384f - w * 0.6f, top, 384f, top);
            face.AddBezier(384f, top, 384f + w * 0.6f, top, 384f + w, 180f, 384f + w, 300f);
            face.AddBezier(384f + w, 300f, 384f + w, 404f, 384f + jaw + 22f, 462f, 384f + jaw * 0.45f, chin);
            face.AddBezier(384f + jaw * 0.45f, chin, 384f + 8f, chin + 7f, 384f - 8f, chin + 7f, 384f - jaw * 0.45f, chin);
            face.AddBezier(384f - jaw * 0.45f, chin, 384f - jaw - 22f, 462f, 384f - w, 404f, 384f - w, 300f);
            face.CloseFigure();

            if (c.longHair && !c.bald) LongHairBehind(w);
            using (var b = new SolidBrush(Dark(c.skin, 0.2f)))
            {
                g.FillEllipse(b, 384f - w - 15f, 288f, 34f, 74f);
                g.FillEllipse(b, 384f + w - 19f, 288f, 34f, 74f);
            }
            using (var b = new LinearGradientBrush(new Rectangle(250, 140, 268, 370), Light(c.skin, 0.1f), Dark(c.skin, 0.28f), 22f))
                g.FillPath(b, face);

            GraphicsState was = g.Save();
            g.SetClip(face);
            using (var shade = new LinearGradientBrush(new RectangleF(384f, 140f, w + 2f, 380f), Fade(Dark(c.second, 0.4f), 0), Fade(Dark(c.second, 0.4f), 130), 0f))
                g.FillRectangle(shade, 384f, 140f, w, 380f);
            if (c.beard)
                using (var b = new SolidBrush(c.hair))
                    g.FillPolygon(b, new[] { new PointF(250, 380), new PointF(320, 402), new PointF(384, 392), new PointF(448, 402), new PointF(518, 380), new PointF(518, 530), new PointF(250, 530) });
            if (c.blotched) Blotches();
            if (c.extraEye) ExtraEye();
            g.Restore(was);

            Features(w);
            if (c.scar)
                using (var p = new Pen(Mix(c.skin, Color.FromArgb(120, 20, 30), 0.65f), 6f))
                {
                    float x = c.roll.Next(2) == 0 ? 318f : 450f;
                    g.DrawLine(p, x - 14f, 232f, x + 22f, 356f);
                    g.DrawLine(p, x - 6f, 268f, x + 12f, 262f);
                    g.DrawLine(p, x + 6f, 312f, x + 24f, 306f);
                }
            if (c.headwear != Headwear.Helmet && !c.bald) HairOnTop(w);
            bool onEyes = c.headwear == Headwear.Glasses || c.headwear == Headwear.Visor;
            if (c.head != null && Item(c.head, 384f, onEyes ? 300f : 180f, onEyes ? 270f : 390f, 0f, false)) { }
            else if (c.headwear == Headwear.Helmet) Helmet(w);
            else if (c.headwear == Headwear.Hat) Hat(w);
            else if (c.headwear == Headwear.Glasses) Glasses();
            else if (c.headwear == Headwear.Visor) Visor(w);
            face.Dispose();
        }

        private void Features(float w)
        {
            float spread = 58f, y = 298f, open = c.feminine ? 13f : 11f, eyeW = c.feminine ? 33f : 29f;
            Color iris = c.formed ? c.glow : c.eyes;
            using (var white = new SolidBrush(c.formed ? Light(c.glow, 0.55f) : Color.FromArgb(236, 234, 228)))
            using (var eye = new SolidBrush(iris))
            using (var black = new SolidBrush(Color.FromArgb(14, 14, 20)))
            using (var lid = new Pen(Dark(c.skin, 0.72f), c.feminine ? 7f : 5f))
            using (var brow = new Pen(Dark(c.hair, 0.15f), c.feminine ? 8f : 13f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                foreach (int side in new[] { -1, 1 })
                {
                    float x = 384f + side * spread;
                    g.FillEllipse(white, x - eyeW, y - open, eyeW * 2f, open * 2f);
                    g.FillEllipse(eye, x - 13f, y - 13f, 26f, 26f);
                    if (!c.formed) g.FillEllipse(black, x - 6f, y - 6f, 12f, 12f);
                    g.DrawArc(lid, x - eyeW, y - open - 2f, eyeW * 2f, open * 2.3f, 190f, 160f);
                    g.DrawLine(brow, x - side * 40f, y - (c.feminine ? 42f : 34f), x + side * 42f, y - (c.feminine ? 54f : 44f));
                    if (c.formed) Glow(x, y, 70f, c.glow, 190);
                    if (c.hurt)
                        using (var p = new Pen(Fade(Color.FromArgb(60, 20, 70), 150), 8f))
                            g.DrawArc(p, x - eyeW, y + 6f, eyeW * 2f, 26f, 15f, 150f);
                }
            using (var p = new Pen(Dark(c.skin, 0.45f), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(p, 392f, 306f, 404f, 372f);
                g.DrawArc(p, 366f, 362f, 40f, 22f, 15f, 150f);
            }
            const float mouth = 428f;
            using (var lips = new SolidBrush(Mix(c.skin, Color.FromArgb(140, 50, 60), c.feminine ? 0.62f : 0.32f)))
                g.FillPolygon(lips, new[] { new PointF(344, mouth), new PointF(372, mouth - 9f), new PointF(384, mouth - 5f), new PointF(396, mouth - 9f), new PointF(424, mouth), new PointF(396, mouth + 11f), new PointF(372, mouth + 11f) });
        }

        private void Glasses()
        {
            Color lens = c.head.trim;
            using (var fill = new SolidBrush(Fade(lens, 110)))
            using (var frame = new Pen(c.head.main, 10f))
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float x = 384f + side * 58f;
                    g.FillEllipse(fill, x - 46f, 298f - 42f, 92f, 84f);
                    g.DrawEllipse(frame, x - 46f, 298f - 42f, 92f, 84f);
                    g.DrawLine(frame, x + side * 46f, 290f, x + side * 76f, 280f);
                }
                g.DrawArc(frame, 372f, 276f, 24f, 24f, 200f, 140f);
            }
        }

        private void Visor(float w)
        {
            using (var band = new SolidBrush(Dark(c.head.main, 0.3f))) g.FillRectangle(band, 384f - w - 8f, 262f, (w + 8f) * 2f, 74f);
            using (var glass = new SolidBrush(Fade(c.head.trim, 215))) g.FillRectangle(glass, 384f - w + 16f, 274f, (w - 16f) * 2f, 50f);
            Glow(384f, 298f, 130f, c.head.trim, 120);
        }

        private void Hat(float w)
        {
            using (var path = new GraphicsPath())
            {
                path.AddBezier(384f - w - 4f, 214f, 384f - w, 120f, 384f - w * 0.5f, 92f, 384f, 92f);
                path.AddBezier(384f, 92f, 384f + w * 0.5f, 92f, 384f + w, 120f, 384f + w + 4f, 214f);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new Rectangle(240, 90, 290, 130), Light(c.head.main, 0.15f), Dark(c.head.main, 0.45f), 60f))
                    g.FillPath(b, path);
            }
            using (var b = new SolidBrush(c.head.trim)) g.FillEllipse(b, 384f - w - 36f, 196f, (w + 36f) * 2f, 36f);
        }

        /// <summary>A third eye in the forehead.</summary>
        private void ExtraEye()
        {
            using (var white = new SolidBrush(Color.FromArgb(240, 236, 170)))
            using (var black = new SolidBrush(Color.Black))
            {
                g.FillEllipse(white, 362f, 196f, 44f, 26f);
                g.FillEllipse(black, 376f, 200f, 16f, 18f);
            }
        }

        /// <summary>Wet blotches: any other mutation shows on the skin.</summary>
        private void Blotches()
        {
            Color flesh = Color.FromArgb(120, 190, 80);
            using (var b = new SolidBrush(Fade(flesh, 170)))
            using (var p = new Pen(Fade(Dark(flesh, 0.5f), 200), 5f))
                for (int i = 0; i < 7; i++)
                {
                    float x = R(270f, 500f), y = R(170f, 480f), r = R(14f, 42f);
                    g.FillEllipse(b, x - r, y - r, r * 2f, r * 2f);
                    g.DrawLine(p, x, y, x + R(-60f, 60f), y + R(-60f, 60f));
                }
        }

        private void LongHairBehind(float w)
        {
            using (var b = new LinearGradientBrush(new Rectangle(230, 150, 310, 480), Light(c.hair, 0.1f), Dark(c.hair, 0.5f), 80f))
            {
                float end = R(510f, 590f);
                g.FillPolygon(b, new[]
                {
                    new PointF(384f - w - 22f, 240f), new PointF(384f - w - 48f, 460f), new PointF(384f - w - 16f, end),
                    new PointF(384f - w * 0.45f, end - 50f), new PointF(384f + w * 0.45f, end - 50f),
                    new PointF(384f + w + 16f, end), new PointF(384f + w + 48f, 460f), new PointF(384f + w + 22f, 240f),
                });
            }
        }

        private void HairOnTop(float w)
        {
            using (var path = new GraphicsPath())
            {
                float crown = 124f - R(0f, 34f), fringe = R(206f, 246f), part = R(-60f, 60f);
                path.AddBezier(384f - w - 12f, 320f, 384f - w - 36f, 190f, 384f - w * 0.6f, crown, 384f, crown);
                path.AddBezier(384f, crown, 384f + w * 0.6f, crown, 384f + w + 36f, 190f, 384f + w + 12f, 320f);
                path.AddBezier(384f + w + 12f, 320f, 384f + w - 14f, 270f, 384f + w * 0.72f, 232f, 384f + part, fringe - 34f);
                path.AddBezier(384f + part, fringe - 34f, 384f - 40f, fringe + 8f, 384f - w * 0.72f, fringe + 14f, 384f - w - 12f, 320f);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new Rectangle(230, 90, 310, 240), Light(c.hair, 0.28f), Dark(c.hair, 0.38f), 72f))
                    g.FillPath(b, path);
            }
        }

        private void Helmet(float w)
        {
            Color shell = c.head.main;
            using (var path = new GraphicsPath())
            {
                path.AddBezier(384f - w - 22f, 368f, 384f - w - 34f, 160f, 384f - w * 0.65f, 104f, 384f, 104f);
                path.AddBezier(384f, 104f, 384f + w * 0.65f, 104f, 384f + w + 34f, 160f, 384f + w + 22f, 368f);
                path.AddLine(384f + w + 22f, 368f, 384f + w - 18f, 340f);
                path.AddLine(384f + w - 18f, 340f, 384f + w - 18f, 256f);
                path.AddLine(384f + w - 18f, 256f, 384f - w + 18f, 256f);
                path.AddLine(384f - w + 18f, 256f, 384f - w + 18f, 340f);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new Rectangle(230, 100, 310, 270), Light(shell, 0.2f), Dark(shell, 0.55f), 40f))
                    g.FillPath(b, path);
                using (var p = new Pen(c.head.trim, 5f)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(c.head.trim)) g.FillRectangle(b, 366f, 130f, 36f, 92f);
        }

        private void Motes()
        {
            using (var b = new SolidBrush(Light(c.glow, 0.5f)))
                for (int i = 0; i < 46; i++)
                {
                    float r = R(2f, 7f);
                    g.FillEllipse(b, R(30f, 740f), R(30f, 740f), r, r);
                }
        }

        /// <summary>Rim light, scanlines and a vignette tie the shapes into one picture.</summary>
        private void Finish()
        {
            using (var b = new LinearGradientBrush(new Rectangle(500, 0, 268, 768), Fade(c.accent, 0), Fade(c.accent, 70), 0f))
                g.FillRectangle(b, 500, 0, 268, 768);
            using (var p = new Pen(Color.FromArgb(18, 0, 0, 0), 2f))
                for (int y = 0; y < 768; y += 4) g.DrawLine(p, 0, y, 768, y);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-170, -170, 1108, 1108);
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(235, 0, 0, 0) }, FocusScales = new PointF(0.55f, 0.55f) })
                    g.FillRectangle(b, 0, 0, 768, 768);
            }
        }
    }
}
