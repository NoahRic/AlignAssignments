using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

using System.Linq;

namespace NoahRichards.AlignAssignments
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    class VsTextViewCreationListener : IVsTextViewCreationListener
    {
        [Import]
        IVsEditorAdaptersFactoryService AdaptersFactory = null;

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            var wpfTextView = AdaptersFactory.GetWpfTextView(textViewAdapter);
            if (wpfTextView == null)
            {
                Debug.Fail("Unable to get IWpfTextView from text view adapter");
                return;
            }

            CommandFilter filter = new CommandFilter(wpfTextView);

            IOleCommandTarget next;
            if (ErrorHandler.Succeeded(textViewAdapter.AddCommandFilter(filter, out next)))
                filter.Next = next;
        }
    }

    class CommandFilter : IOleCommandTarget
    {
        IWpfTextView _view;

        public CommandFilter(IWpfTextView view)
        {
            _view = view;
        }

        internal IOleCommandTarget Next { get; set; }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (pguidCmdGroup == GuidList.guidAlignAssignmentsCmdSet &&
                nCmdID == PkgCmdIDList.cmdidAlignAssignments)
            {
                AlignAssignments();
                return VSConstants.S_OK;
            }

            return Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            if (pguidCmdGroup == GuidList.guidAlignAssignmentsCmdSet &&
                prgCmds[0].cmdID == PkgCmdIDList.cmdidAlignAssignments)
            {
                if (AssignmentsToAlign)
                    prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
                else
                    prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED);

                return VSConstants.S_OK;
            }

            return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        private void AlignAssignments()
        {
            // Find all lines above and below with = signs
            ITextSnapshot snapshot = _view.TextSnapshot;

            if (snapshot != snapshot.TextBuffer.CurrentSnapshot)
                return;

            int currentLineNumber = snapshot.GetLineNumberFromPosition(_view.Caret.Position.BufferPosition);

            Dictionary<int, ColumnAndOffset> lineNumberToEqualsColumn = new Dictionary<int, ColumnAndOffset>();

            // Start with the current line
            ColumnAndOffset columnAndOffset = GetColumnNumberOfFirstEquals(snapshot.GetLineFromLineNumber(currentLineNumber));
            if (columnAndOffset.Column == -1)
                return;

            lineNumberToEqualsColumn[currentLineNumber] = columnAndOffset;

            int lineNumber = currentLineNumber;
            int minLineNumber = 0;
            int maxLineNumber = snapshot.LineCount - 1;

            // If the selection spans multiple lines, only attempt to fix the lines in the selection
            if (!_view.Selection.IsEmpty)
            {
                var selectionStartLine = _view.Selection.Start.Position.GetContainingLine();
                if (_view.Selection.End.Position > selectionStartLine.End)
                {
                    minLineNumber = selectionStartLine.LineNumber;
                    maxLineNumber = snapshot.GetLineNumberFromPosition(_view.Selection.End.Position);
                }
            }

            // Moving backwards
            for (lineNumber = currentLineNumber - 1; lineNumber >= minLineNumber; lineNumber--)
            {
                columnAndOffset = GetColumnNumberOfFirstEquals(snapshot.GetLineFromLineNumber(lineNumber));
                if (columnAndOffset.Column == -1)
                    break;

                lineNumberToEqualsColumn[lineNumber] = columnAndOffset;
            }

            // Moving forwards
            for (lineNumber = currentLineNumber + 1; lineNumber <= maxLineNumber; lineNumber++)
            {
                columnAndOffset = GetColumnNumberOfFirstEquals(snapshot.GetLineFromLineNumber(lineNumber));
                if (columnAndOffset.Column == -1)
                    break;

                lineNumberToEqualsColumn[lineNumber] = columnAndOffset;
            }

            // Perform the actual edit
            if (lineNumberToEqualsColumn.Count > 1)
            {
                int columnToIndentTo = lineNumberToEqualsColumn.Values.Max(c => c.Column);

                using (var edit = snapshot.TextBuffer.CreateEdit())
                {
                    foreach (var pair in lineNumberToEqualsColumn.Where(p => p.Value.Column < columnToIndentTo))
                    {
                        ITextSnapshotLine line = snapshot.GetLineFromLineNumber(pair.Key);
                        string spaces = new string(' ', columnToIndentTo - pair.Value.Column);

                        if (!edit.Insert(line.Start.Position + pair.Value.Offset, spaces))
                            return;
                    }

                    edit.Apply();
                }
            }
        }

        private ColumnAndOffset GetColumnNumberOfFirstEquals(ITextSnapshotLine line)
        {
            ITextSnapshot snapshot = line.Snapshot;
            int tabSize = _view.Options.GetOptionValue(DefaultOptions.TabSizeOptionId);

            int column = 0;
            int nonWhiteSpaceCount = 0;
            for (int i = line.Start.Position; i < line.End.Position; i++)
            {
                char ch = snapshot[i];
                if (ch == '=')
                    return new ColumnAndOffset() { Column = column, 
                                                   Offset = (i - line.Start.Position) - nonWhiteSpaceCount };

                if (ch == '\t' || ch == ' ')
                    nonWhiteSpaceCount = 0;
                else
                    nonWhiteSpaceCount++;

                if (ch == '\t')
                    column += tabSize - (column % tabSize);
                else
                    column++;
            }

            return new ColumnAndOffset() { Column = -1, Offset = -1 };
        }

        struct ColumnAndOffset
        {
            public int Column;
            public int Offset;
        }

        private bool AssignmentsToAlign
        {
            get
            {
                return _view.Caret.Position.BufferPosition.GetContainingLine().GetText().Contains("=");
            }
        }
    }
}