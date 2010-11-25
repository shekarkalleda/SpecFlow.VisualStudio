﻿using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace TechTalk.SpecFlow.Vs2010Integration.AutoComplete
{
    internal abstract class CompletionCommandFilter : IOleCommandTarget
    {
        private ICompletionSession currentAutoCompleteSession = null;

        public IWpfTextView TextView { get; private set; }
        public ICompletionBroker Broker { get; private set; }

        public IOleCommandTarget Next { get; set; }

        protected CompletionCommandFilter(IWpfTextView textView, ICompletionBroker broker)
        {
            TextView = textView;
            Broker = broker;
        }

        private char GetTypeChar(IntPtr pvaIn)
        {
            return (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            bool handled = false;
            int hresult = VSConstants.S_OK;

            // 1. Pre-process
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                        handled = StartAutoCompleteSession();
                        break;
                    case VSConstants.VSStd2KCmdID.RETURN:
                        handled = CommitAutoComplete(false);
                        break;
                    case VSConstants.VSStd2KCmdID.TAB:
                        handled = CommitAutoComplete(true);
                        break;
                    case VSConstants.VSStd2KCmdID.CANCEL:
                        handled = CancelAutoComplete();
                        break;
                }
            }

            if (!handled)
                hresult = Next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            if (ErrorHandler.Succeeded(hresult))
            {
                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    switch ((VSConstants.VSStd2KCmdID)nCmdID)
                    {
                        case VSConstants.VSStd2KCmdID.TYPECHAR:
                            var ch = GetTypeChar(pvaIn);
                            if (ch == ' ')
                                StartAutoCompleteSession();
                            else 
                                FilterAutoComplete();
                            break;
                        case VSConstants.VSStd2KCmdID.BACKSPACE:
                            FilterAutoComplete();
                            break;
                    }
                }
            }

            return hresult;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)prgCmds[0].cmdID)
                {
                    case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                        prgCmds[0].cmdf = (uint)OLECMDF.OLECMDF_ENABLED | (uint)OLECMDF.OLECMDF_SUPPORTED;
                        return VSConstants.S_OK;
                }
            }
            return Next.QueryStatus(pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        #region AutoComplete Events

        protected bool IsAutoCompleteSessionActive
        {
            get { return currentAutoCompleteSession != null && currentAutoCompleteSession.IsStarted && !currentAutoCompleteSession.IsDismissed; }
        }

        protected bool StartAutoCompleteSession()
        {
            if (IsAutoCompleteSessionActive)
                return false;

            SnapshotPoint caret = TextView.Caret.Position.BufferPosition;
            ITextSnapshot snapshot = caret.Snapshot;

            if (!ShouldCompletionBeDiplayed(caret)) 
                return false;

            currentAutoCompleteSession = !Broker.IsCompletionActive(TextView) ? 
                                                                                  Broker.CreateCompletionSession(TextView, snapshot.CreateTrackingPoint(caret, PointTrackingMode.Positive), true) : 
                                                                                                                                                                                                      Broker.GetSessions(TextView)[0];

            currentAutoCompleteSession.Start();
            currentAutoCompleteSession.Dismissed += (sender, args) => currentAutoCompleteSession = null;

            return true;
        }

        protected void FilterAutoComplete()
        {
            if (!IsAutoCompleteSessionActive)
                return;

            currentAutoCompleteSession.SelectedCompletionSet.SelectBestMatch();
            currentAutoCompleteSession.SelectedCompletionSet.Recalculate();
        }

        protected bool CommitAutoComplete(bool force)
        {
            if (!IsAutoCompleteSessionActive)
                return false;

            if (!currentAutoCompleteSession.SelectedCompletionSet.SelectionStatus.IsSelected && !force)
            {
                currentAutoCompleteSession.Dismiss();
                return false;
            }

            currentAutoCompleteSession.Commit();
            return true;
        }

        protected bool CancelAutoComplete()
        {
            if (!IsAutoCompleteSessionActive)
                return false;

            currentAutoCompleteSession.Dismiss();
            return true;
        }

        /// <summary>
        /// Returns true if completion should be showed or not 
        /// </summary>
        protected abstract bool ShouldCompletionBeDiplayed(SnapshotPoint caret);

        #endregion
    }
}