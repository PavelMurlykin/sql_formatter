using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TSqlFormatter.IdeShared;

/// <summary>Underline markers for analysis findings in an editor buffer; cleared on demand.</summary>
public sealed class SqlAnalysisMarkers
{
    private readonly List<IVsTextLineMarker> markers = new List<IVsTextLineMarker>();

    public int Count => markers.Count;

    public void Apply(IVsTextLines buffer, SqlDocumentAnalysis analysis)
    {
        Clear();
        if (buffer is null || analysis is null || analysis.LineMap is null)
        {
            return;
        }

        foreach (var diagnostic in analysis.Diagnostics)
        {
            var end = analysis.LineMap.GetLinePosition(diagnostic.Span.EndOffset);
            var slot = new IVsTextLineMarker[1];
            var hr = buffer.CreateLineMarker(
                (int)MARKERTYPE.MARKER_CODESENSE_ERROR,
                diagnostic.Line - 1,
                diagnostic.Column - 1,
                end.Line - 1,
                end.Column - 1,
                null,
                slot);
            if (ErrorHandler.Succeeded(hr) && slot[0] is not null)
            {
                markers.Add(slot[0]);
            }
        }
    }

    public void Clear()
    {
        foreach (var marker in markers)
        {
            try
            {
                marker.Invalidate();
            }
            catch (Exception)
            {
                // A marker can already be gone when its buffer was closed.
            }
        }

        markers.Clear();
    }
}
