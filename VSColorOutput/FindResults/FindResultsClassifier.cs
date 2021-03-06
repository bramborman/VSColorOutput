using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using VSColorOutput.Output.ColorClassifier;
using VSColorOutput.State;

// Disable warning for unused ClassificationChanged event
#pragma warning disable 67

namespace VSColorOutput.FindResults
{
    public class FindResultsClassifier : IClassifier
    {
        private int _initialized;
        private const string FindAll = "Find all \"";
        private const string MatchCase = "Match case";
        private const string WholeWord = "Whole word";
        private const string ListFilenamesOnly = "List filenames only";

        private bool _settingsLoaded;
        private IClassificationTypeRegistryService _classificationRegistry;
        private IClassificationFormatMapService _formatMapService;
        private static readonly Regex FilenameRegex = new Regex(@"^\s*.[:\\]\\.*\(\d+\):", RegexOptions.Compiled);

        private Regex _searchTextRegex;

        public bool HighlightFindResults { get; set; }

        public void Initialize(IClassificationTypeRegistryService classificationRegistry, IClassificationFormatMapService formatMapService)
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 1) return;

            try
            {
                _classificationRegistry = classificationRegistry;
                _formatMapService = formatMapService;

                Settings.SettingsUpdated += (sender, args) =>
                {
                    _settingsLoaded = false;
                    UpdateFormatMap();
                };
            }
            catch (Exception ex)
            {
                Log.LogError(ex.ToString());
                throw;
            }
        }

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            LoadSettings();

            var snapshot = span.Snapshot;
            if (snapshot == null || snapshot.Length == 0 || !CanSearch(span) || !HighlightFindResults)
            {
                return Array.Empty<ClassificationSpan>();
            }

            var text = span.GetText();

            var filenameSpans = GetMatches(text, FilenameRegex, span.Start, FilenameClassificationType);
            var searchTermSpans = GetMatches(text, _searchTextRegex, span.Start, SearchTermClassificationType);

            var toRemove = from searchSpan in searchTermSpans
                from filenameSpan in filenameSpans
                where filenameSpan.Span.Contains(searchSpan.Span)
                select searchSpan;

            var classifications = new List<ClassificationSpan>(filenameSpans);
            classifications.AddRange(searchTermSpans.Except(toRemove));
            return classifications;
        }

        private bool CanSearch(SnapshotSpan span)
        {
            if (span.Start.Position != 0 && _searchTextRegex != null)
            {
                return true;
            }
            _searchTextRegex = null;
            var firstLine = span.Snapshot.GetLineFromLineNumber(0).GetText();
            if (firstLine.StartsWith(FindAll))
            {
                var strings = Array.ConvertAll(firstLine.Split(','), s => s.Trim());

                var start = strings[0].IndexOf('"');
                var length = strings[0].Length - start - 2;
                if (length < 0) return false;
                var searchTerm = strings[0].Substring(start + 1, length);
                var matchCase = strings.Contains(MatchCase);
                var matchWholeWord = strings.Contains(WholeWord);
                var filenamesOnly = strings.Contains(ListFilenamesOnly);

                if (!filenamesOnly)
                {
                    var regex = matchWholeWord ? $@"\b{Regex.Escape(searchTerm)}\b" : Regex.Escape(searchTerm);
                    var casing = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                    _searchTextRegex = new Regex(regex, RegexOptions.None | casing);

                    return true;
                }
            }
            return false;
        }

        private void UpdateFormatMap()
        {
            var colorMap = ColorMap.GetMap();
            var formatMap = _formatMapService.GetClassificationFormatMap("find results");
            try
            {
                var classificationNames = new[]
                {
                    ClassificationTypeDefinitions.FindResultsFilename,
                    ClassificationTypeDefinitions.FindResultsSearchTerm
                };

                formatMap.BeginBatchUpdate();
                foreach (var names in classificationNames)
                {
                    var classificationType = _classificationRegistry.GetClassificationType(names);
                    var textProperties = formatMap.GetTextProperties(classificationType);
                    var color = colorMap[names];
                    var wpfColor = System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
                    formatMap.SetTextProperties(classificationType, textProperties.SetForeground(wpfColor));
                }
            }
            finally
            {
                formatMap.EndBatchUpdate();
            }
        }

        private void LoadSettings()
        {
            if (_settingsLoaded) return;
            var settings = Settings.Load();
            HighlightFindResults = settings.HighlightFindResults;
            _settingsLoaded = true;
        }

        private static ClassificationSpan[] GetMatches(string text, Regex regex, SnapshotPoint snapStart, IClassificationType classificationType)
        {
            var matches = regex.Matches(text);
            var output = new ClassificationSpan[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                output[i] = new ClassificationSpan(new SnapshotSpan(snapStart + match.Index, match.Length), classificationType);
            }
            return output;
        }

        private IClassificationType SearchTermClassificationType => _classificationRegistry.GetClassificationType(ClassificationTypeDefinitions.FindResultsSearchTerm);

        private IClassificationType FilenameClassificationType => _classificationRegistry.GetClassificationType(ClassificationTypeDefinitions.FindResultsFilename);

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;
    }
}