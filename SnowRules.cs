using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NoSnowyBiome
{
    internal sealed class BiomeSnowRule
    {
        public string Biome { get; set; } = "";
        public bool NoSnow { get; set; }
        public float NoSnowMinAltitude { get; set; } = 50f;
    }

    internal static class SnowRules
    {
        internal const int MaximumYamlLength = 1024 * 1024;
        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .Build();

        internal static List<BiomeSnowRule> Parse(string yaml)
        {
            if (yaml == null || yaml.Length > MaximumYamlLength)
                throw new InvalidDataException("Snow rules must be at most 1 MiB of text.");
            List<BiomeSnowRule> rules = Deserializer.Deserialize<List<BiomeSnowRule>>(yaml)
                ?? throw new InvalidDataException("Use [] for an empty rule list.");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BiomeSnowRule rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Biome))
                    throw new InvalidDataException("Every rule needs a biome name.");
                rule.Biome = rule.Biome.Trim();
                if (rule.Biome.Equals("None", StringComparison.OrdinalIgnoreCase) || rule.Biome.Contains(",") ||
                    long.TryParse(rule.Biome, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new InvalidDataException($"Use a single biome name, not a numeric ID or mask: {rule.Biome}");
                if (!names.Add(rule.Biome))
                    throw new InvalidDataException($"Duplicate biome rule: {rule.Biome}");
                if (float.IsNaN(rule.NoSnowMinAltitude) || float.IsInfinity(rule.NoSnowMinAltitude))
                    throw new InvalidDataException($"Invalid minimum altitude for {rule.Biome}.");
            }
            return rules;
        }

        internal static Dictionary<int, BiomeSnowRule> Resolve(IEnumerable<BiomeSnowRule> entries, Func<string, int?> resolve,
            Dictionary<int, BiomeSnowRule>? result = null)
        {
            result = result ?? new Dictionary<int, BiomeSnowRule>();
            result.Clear();
            foreach (BiomeSnowRule entry in entries)
            {
                int? id = resolve(entry.Biome);
                if (id.HasValue && id.Value != 0) result[id.Value] = entry;
            }
            return result;
        }

        internal static bool Equivalent(Dictionary<int, BiomeSnowRule> left, Dictionary<int, BiomeSnowRule> right)
        {
            if (left.Count != right.Count) return false;
            foreach (var entry in left)
                if (!right.TryGetValue(entry.Key, out BiomeSnowRule? other) ||
                    entry.Value.NoSnow != other.NoSnow || entry.Value.NoSnowMinAltitude != other.NoSnowMinAltitude)
                    return false;
            return true;
        }

        internal static bool ShouldSuppress(Dictionary<int, BiomeSnowRule> rules, int biome, float altitude)
        {
            return rules.TryGetValue(biome, out BiomeSnowRule? rule) && rule.NoSnow && altitude >= rule.NoSnowMinAltitude;
        }
    }

    internal sealed class SnowRuleFile
    {
        internal const string DefaultYaml = "# NoSnowyBiome rules. This file is separate from EWD's expand_biomes.yaml.\n" +
            "# Mistlands snow suppression is enabled by default. Use [] to clear all rules.\n" +
            "- biome: Mistlands\n  noSnow: true\n  noSnowMinAltitude: 50\n";
        private readonly string _path;
        private readonly Action<string> _warn;
        private string? _lastAttempt;
        private string? _lastReadError;
        private bool _stampKnown;
        private long _length;
        private long _writeTime;
        private long _creationTime;
        private bool _retryRead;
        internal int ContentReadCount { get; private set; }

        internal SnowRuleFile(string path, Action<string> warn)
        {
            _path = path;
            _warn = warn;
        }

        internal void EnsureExists()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path)) return;
                using (FileStream stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    writer.Write(DefaultYaml);
            }
            catch (IOException) when (File.Exists(_path)) { }
            catch (Exception ex) { _warn($"Could not create {_path}: {ex.Message}"); }
        }

        internal bool TryRead(out string yaml)
        {
            yaml = "";
            try
            {
                FileInfo file = new FileInfo(_path);
                long length = file.Exists ? file.Length : -1;
                long writeTime = file.Exists ? file.LastWriteTimeUtc.Ticks : 0;
                long creationTime = file.Exists ? file.CreationTimeUtc.Ticks : 0;
                if (_stampKnown && !_retryRead && length == _length && writeTime == _writeTime && creationTime == _creationTime)
                    return false;
                _stampKnown = true;
                _length = length;
                _writeTime = writeTime;
                _creationTime = creationTime;
                _retryRead = false;
                if (!file.Exists) throw new FileNotFoundException("File missing; keeping the previous rules. Use [] to clear them.");
                if (file.Length > SnowRules.MaximumYamlLength)
                    throw new InvalidDataException("File exceeds 1 MiB.");
                string text;
                try
                {
                    ContentReadCount++;
                    text = File.ReadAllText(_path);
                }
                catch (IOException) { _retryRead = true; throw; }
                catch (UnauthorizedAccessException) { _retryRead = true; throw; }
                _lastReadError = null;
                if (text == _lastAttempt) return false;
                _lastAttempt = text;
                SnowRules.Parse(text);
                yaml = text;
                return true;
            }
            catch (Exception ex)
            {
                if (_lastReadError != ex.Message)
                    _warn($"Could not load {_path}; keeping the previous rules: {ex.Message}");
                _lastReadError = ex.Message;
                return false;
            }
        }
    }
}
