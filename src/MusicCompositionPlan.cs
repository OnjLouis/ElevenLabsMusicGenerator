using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace ElevenLabsMusicGenerator
{
    internal sealed class MusicSection
    {
        public string Name { get; set; }
        public string Body { get; set; }
        public int DurationMilliseconds { get; set; }
        public int DurationSeconds { get { return DurationMilliseconds / 1000; } set { DurationMilliseconds = value * 1000; } }
        public string PositiveStyles { get; set; }
        public string NegativeStyles { get; set; }
        public string ContextAdherence { get; set; }

        public MusicSection()
        {
            Name = "New section";
            Body = string.Empty;
            DurationSeconds = 15;
            PositiveStyles = string.Empty;
            NegativeStyles = string.Empty;
            ContextAdherence = "high";
        }

        public override string ToString()
        {
            return Name + ", " + (DurationMilliseconds / 1000m).ToString("0.###") + " seconds";
        }

        internal Dictionary<string, object> ToPayload()
        {
            return new Dictionary<string, object>
            {
                { "text", "[" + Name.Trim() + "]" + (string.IsNullOrWhiteSpace(Body) ? string.Empty : "\n" + Body.Trim()) },
                { "duration_ms", DurationMilliseconds },
                { "positive_styles", SplitStyles(PositiveStyles) },
                { "negative_styles", SplitStyles(NegativeStyles) },
                { "context_adherence", ContextAdherence }
            };
        }

        internal static string[] SplitStyles(string value)
        {
            return (value ?? string.Empty).Replace("\r", string.Empty).Split('\n')
                .Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
        }
    }

    internal sealed class MusicCompositionPlan
    {
        public List<MusicSection> Sections { get; private set; }
        public int TotalMilliseconds { get { return Sections.Sum(section => section.DurationMilliseconds); } }
        public int TotalSeconds { get { return (int)Math.Ceiling(TotalMilliseconds / 1000m); } }

        public MusicCompositionPlan()
        {
            Sections = new List<MusicSection>();
        }

        public void Validate()
        {
            if (Sections.Count < 1 || Sections.Count > 30) throw new InvalidDataException("A composition plan needs 1 to 30 sections.");
            foreach (var section in Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Name) || section.Name.Contains("[") || section.Name.Contains("]") || section.Name.Contains("\n"))
                    throw new InvalidDataException("Each section needs a name without brackets or line breaks.");
                if (section.DurationMilliseconds < 3000 || section.DurationMilliseconds > 120000)
                    throw new InvalidDataException("Each section must last between 3 and 120 seconds.");
                if (MusicSection.SplitStyles(section.PositiveStyles).Length > 50 || MusicSection.SplitStyles(section.NegativeStyles).Length > 50)
                    throw new InvalidDataException("A section cannot contain more than 50 included or excluded styles.");
                if (section.ContextAdherence != "low" && section.ContextAdherence != "medium" && section.ContextAdherence != "high")
                    throw new InvalidDataException("Section context adherence must be low, medium or high.");
                if (section.ToPayload()["text"].ToString().Length > 6000)
                    throw new InvalidDataException("Section text is too long. Keep it below 6,000 characters.");
            }
            if (TotalMilliseconds < 3000 || TotalMilliseconds > 600000)
                throw new InvalidDataException("The complete plan must last between 3 seconds and 10 minutes.");
        }

        public Dictionary<string, object> ToPayload()
        {
            Validate();
            return new Dictionary<string, object> { { "chunks", Sections.Select(section => section.ToPayload()).ToArray() } };
        }

        public string ToJson()
        {
            return new JavaScriptSerializer().Serialize(ToPayload());
        }

        public static MusicCompositionPlan FromJson(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object nestedPlan;
            if (root != null && !root.ContainsKey("chunks") && root.TryGetValue("composition_plan", out nestedPlan))
                root = nestedPlan as Dictionary<string, object>;
            object rawChunks;
            var chunks = root != null && root.TryGetValue("chunks", out rawChunks) ? rawChunks as object[] : null;
            if (chunks == null) throw new InvalidDataException("This JSON file does not contain a reusable v2 or v2.5 composition plan.");
            var plan = new MusicCompositionPlan();
            foreach (var rawChunk in chunks)
            {
                var chunk = rawChunk as Dictionary<string, object>;
                if (chunk == null || !chunk.ContainsKey("text") || !chunk.ContainsKey("duration_ms"))
                    throw new InvalidDataException("The plan contains an unsupported audio-reference section.");
                var text = Convert.ToString(chunk["text"]) ?? string.Empty;
                var closing = text.IndexOf(']');
                if (!text.StartsWith("[", StringComparison.Ordinal) || closing < 2)
                    throw new InvalidDataException("A section heading must begin with [Name].");
                plan.Sections.Add(new MusicSection
                {
                    Name = text.Substring(1, closing - 1),
                    Body = text.Substring(closing + 1).TrimStart('\r', '\n'),
                    DurationMilliseconds = Convert.ToInt32(chunk["duration_ms"]),
                    PositiveStyles = ReadStyles(chunk, "positive_styles"),
                    NegativeStyles = ReadStyles(chunk, "negative_styles"),
                    ContextAdherence = chunk.ContainsKey("context_adherence") ? Convert.ToString(chunk["context_adherence"]) : "high"
                });
            }
            plan.Validate();
            return plan;
        }

        private static string ReadStyles(Dictionary<string, object> chunk, string key)
        {
            object raw;
            var items = chunk.TryGetValue(key, out raw) ? raw as object[] : null;
            return items == null ? string.Empty : string.Join("\n", items.Select(Convert.ToString).ToArray());
        }
    }
}
