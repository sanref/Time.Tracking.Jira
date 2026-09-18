/**************************************************************************
Copyright 2016 Carsten Gehling

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
**************************************************************************/
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Time.Tracking.Jira
{
    static public class JiraTimeHelpers
    {
        public static TimeTrackingConfiguration Configuration { get; set; }

        public static string DateTimeToJiraDateTime(DateTimeOffset date)
        {
            string formatted = date.ToString("yyyy-MM-dd\\THH:mm:ss.fffzzzz", CultureInfo.InvariantCulture);
            return formatted.Substring(0, formatted.Length - 3) + formatted.Substring(formatted.Length - 2);
        }

        public static string TimeSpanToJiraTime(TimeSpan ts)
        {
            // Always calculate total minutes first
            long totalMinutes = (long)ts.TotalMinutes;
            
            if (Configuration == null)
            {
                // Without configuration, use standard 24-hour days
                long days = totalMinutes / (24 * 60);
                long hours = (totalMinutes % (24 * 60)) / 60;
                long minutes = totalMinutes % 60;

                if (days > 0)
                    return String.Format("{0}d {1}h {2}m", days, hours, minutes);

                if (hours > 0)
                    return String.Format("{0}h {1}m", hours, minutes);

                return String.Format("{0}m", minutes);
            }
            else
            {
                // With configuration, use working hours per day
                long minutesPerDay = (long)(Configuration.workingHoursPerDay * 60);
                long days = totalMinutes / minutesPerDay;
                long remainingMinutes = totalMinutes % minutesPerDay;
                long hours = remainingMinutes / 60;
                long minutes = remainingMinutes % 60;

                if (days > 0)
                {
                    return String.Format("{0}d {1}h {2}m", days, hours, minutes);
                }
                else if (hours > 0)
                {
                    return String.Format("{0}h {1}m", hours, minutes);
                }
                else
                {
                    return String.Format("{0}m", minutes);
                }
            }

        }


        public static TimeSpan? JiraTimeToTimeSpan(string time)
        {
            string s;
            decimal t;
            long minutes = 0;  // Changed from int to long to handle large values
            bool validFormat = true;

            time = time.Trim();

            if (time == "0")
                return TimeSpan.Zero;

            MatchCollection matches = new Regex(@"([0-9,\.]+[dhm] *?)+?", RegexOptions.IgnoreCase).Matches(time);
            if (matches.Count == 0)
                return null;

            foreach (Match match in matches)
            {
                s = match.Value.ToUpper();
                s = s.Trim();

                if (!s.Contains("M") && !s.Contains("H") && !s.Contains("D"))
                {
                    validFormat = false;
                    break;
                }

                if (!decimal.TryParse(s.Replace("M", "").Replace("H", "").Replace("D", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out t))
                {
                    validFormat = false;
                    break;
                }

                try
                {
                    if (s.Contains("M"))
                        minutes += (long)Math.Floor(t);

                    if (s.Contains("H"))
                        minutes += (long)Math.Floor(t * 60);

                    if (s.Contains("D"))
                    {
                        // Use working hours per day if configuration exists, otherwise use 24 hours
                        long minutesPerDay = Configuration != null 
                            ? (long)(Configuration.workingHoursPerDay * 60) 
                            : 24 * 60;
                        minutes += (long)Math.Floor(t * minutesPerDay);
                    }
                }
                catch (OverflowException)
                {
                    // Value is too large to be represented as a TimeSpan
                    return null;
                }
            }

            if (!validFormat)
                return null;

            // Check if the total minutes is within TimeSpan's valid range
            // TimeSpan.MaxValue.TotalMinutes is approximately 1.5378e+11 minutes
            if (minutes > TimeSpan.MaxValue.TotalMinutes || minutes < TimeSpan.MinValue.TotalMinutes)
                return null;

            try
            {
                // Use TimeSpan.FromMinutes to safely create the TimeSpan
                return TimeSpan.FromMinutes(minutes);
            }
            catch (OverflowException)
            {
                // Value is too large to be represented as a TimeSpan
                return null;
            }
        }
    }
}

