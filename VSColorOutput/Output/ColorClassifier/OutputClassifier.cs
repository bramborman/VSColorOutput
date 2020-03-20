using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using VSColorOutput.State;

// ReSharper disable EmptyGeneralCatchClause
#pragma warning disable 67

namespace VSColorOutput.Output.ColorClassifier
{
    public class OutputClassifier : IClassifier
    {
        private int _initialized;
        private IList<Classifier> _classifiers;
        private IClassificationTypeRegistryService _classificationTypeRegistry;
        private IClassificationFormatMapService _formatMapService;

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        public void Initialize(IClassificationTypeRegistryService registry, IClassificationFormatMapService formatMapService)
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 1) return;
            try
            {
                _classificationTypeRegistry = registry;
                _formatMapService = formatMapService;

                Settings.SettingsUpdated += (sender, args) =>
                {
                    UpdateClassifiers();
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
            try
            {
                var snapshot = span.Snapshot;
                if (snapshot == null || snapshot.Length == 0) return Array.Empty<ClassificationSpan>();
                if (_classifiers == null) UpdateClassifiers();

                var classifiers = _classifiers ?? Array.Empty<Classifier>();
                var start = span.Start.GetContainingLine().LineNumber;
                var end = (span.End - 1).GetContainingLine().LineNumber;
                var spans = new List<ClassificationSpan>();
                for (var i = start; i <= end; i++)
                {
                    var line = snapshot.GetLineFromLineNumber(i);
                    if (line == null) continue;
                    var snapshotSpan = new SnapshotSpan(line.Start, line.Length);
                    var text = line.Snapshot.GetText(snapshotSpan);
                    if (string.IsNullOrEmpty(text)) continue;

                    var classificationName = classifiers.First(classifier => classifier.Test(text)).Type;
                    var type = _classificationTypeRegistry.GetClassificationType(classificationName);
                    if (type != null) spans.Add(new ClassificationSpan(line.Extent, type));
                }
                return spans;
            }
            catch (RegexMatchTimeoutException)
            {
                // eat it.
                return Array.Empty<ClassificationSpan>();
            }
            catch (NullReferenceException)
            {
                // eat it.
                return Array.Empty<ClassificationSpan>();
            }
            catch (Exception ex)
            {
                Log.LogError(ex.ToString());
                throw;
            }
        }

        private void UpdateClassifiers()
        {
            var settings = Settings.Load();
            var patterns = settings.Patterns ?? Array.Empty<RegExClassification>();

            _classifiers = patterns.Select(
                pattern => new Classifier
                {
                    Type = pattern.ClassificationType.ToString(),
                    Test = RegExClassification.RegExFactory(pattern).IsMatch
                })
                .Append(new Classifier
                {
                    Type = ClassificationTypeDefinitions.BuildText,
                    Test = t => true
                })
                .ToList();
        }

        private void UpdateFormatMap()
        {
            var colorMap = ColorMap.GetMap();
            var formatMap = _formatMapService.GetClassificationFormatMap("output");
            try
            {
                var classificationNames = new[]
                {
                    ClassificationTypeDefinitions.BuildHead,
                    ClassificationTypeDefinitions.BuildText,
                    ClassificationTypeDefinitions.LogInfo,
                    ClassificationTypeDefinitions.LogWarn,
                    ClassificationTypeDefinitions.LogError,
                    ClassificationTypeDefinitions.LogCustom1,
                    ClassificationTypeDefinitions.LogCustom2,
                    ClassificationTypeDefinitions.LogCustom3,
                    ClassificationTypeDefinitions.LogCustom4
                };

                formatMap.BeginBatchUpdate();
                foreach (var name in classificationNames)
                {
                    var classificationType = _classificationTypeRegistry.GetClassificationType(name);
                    var textProperties = formatMap.GetTextProperties(classificationType);
                    var color = colorMap[name];
                    var wpfColor = ClassificationTypeDefinitions.ToMediaColor(color);
                    formatMap.SetTextProperties(classificationType, textProperties.SetForeground(wpfColor));
                }
            }
            finally
            {
                formatMap.EndBatchUpdate();
            }
        }
    }
}