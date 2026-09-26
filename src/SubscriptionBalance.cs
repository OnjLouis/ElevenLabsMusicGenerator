using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace ElevenLabsMusicGenerator
{
    internal sealed class SubscriptionBalance
    {
        public long Used { get; private set; }
        public long Limit { get; private set; }
        public long Remaining { get { return Math.Max(0, Limit - Used); } }
        public DateTimeOffset? NextReset { get; private set; }

        public static SubscriptionBalance Parse(string json)
        {
            Dictionary<string, object> values;
            try { values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch (Exception ex) { throw new InvalidDataException("ElevenLabs returned invalid subscription data.", ex); }
            if (values == null) throw new InvalidDataException("ElevenLabs returned no subscription data.");
            var used = RequiredCount(values, "character_count");
            var limit = RequiredCount(values, "character_limit");
            DateTimeOffset? reset = null;
            object rawReset;
            if (values.TryGetValue("next_character_count_reset_unix", out rawReset) && rawReset != null)
            {
                try
                {
                    var seconds = Convert.ToInt64(rawReset, CultureInfo.InvariantCulture);
                    if (seconds <= 0) throw new ArgumentOutOfRangeException("next_character_count_reset_unix");
                    reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
                catch (Exception ex) { throw new InvalidDataException("ElevenLabs returned an invalid credit reset date.", ex); }
            }
            return new SubscriptionBalance { Used = used, Limit = limit, NextReset = reset };
        }

        public string Format(DateTimeOffset now)
        {
            var lines = "Included credits remaining: " + Remaining.ToString("N0", CultureInfo.CurrentCulture) +
                " of " + Limit.ToString("N0", CultureInfo.CurrentCulture) + "." + Environment.NewLine +
                "Used this period: " + Used.ToString("N0", CultureInfo.CurrentCulture) + ".";
            if (!NextReset.HasValue) return lines + Environment.NewLine + "Next reset: unavailable.";
            var reset = NextReset.Value.ToLocalTime();
            lines += Environment.NewLine + "Next reset: " + reset.ToString("d MMM yyyy, HH:mm zzz", CultureInfo.CurrentCulture) + " (local time).";
            var remaining = NextReset.Value - now;
            if (remaining > TimeSpan.Zero)
            {
                var minutes = (long)Math.Ceiling(remaining.TotalMinutes);
                var days = minutes / 1440;
                var hours = minutes % 1440 / 60;
                var parts = new List<string>();
                if (days > 0) parts.Add(days + (days == 1 ? " day" : " days"));
                if (hours > 0) parts.Add(hours + (hours == 1 ? " hour" : " hours"));
                if (minutes % 60 > 0) parts.Add(minutes % 60 + (minutes % 60 == 1 ? " minute" : " minutes"));
                lines += " Approximately " + string.Join(", ", parts.ToArray()) + " remaining.";
            }
            return lines;
        }

        private static long RequiredCount(Dictionary<string, object> values, string name)
        {
            object value;
            if (!values.TryGetValue(name, out value) || value == null) throw new InvalidDataException("The subscription response did not include " + name + ".");
            long count;
            try { count = Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch (Exception ex) { throw new InvalidDataException("The subscription response has an invalid " + name + ".", ex); }
            if (count < 0) throw new InvalidDataException("The subscription response has a negative " + name + ".");
            return count;
        }
    }
}
