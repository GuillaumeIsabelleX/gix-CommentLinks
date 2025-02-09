﻿// Copyright (c) Matt Lacey Ltd. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Button = System.Windows.Controls.Button;

namespace CommentLinks
{
    internal sealed class CommentLinkAdornment : Button
    {
        private readonly int currentLineNumber;

        internal CommentLinkAdornment(CommentLinkTag tag, int currentLineNumber)
        {
            this.Content = new TextBlock { Text = "➡" };
            this.BorderBrush = null;
            this.Padding = new Thickness(0);
            this.Margin = new Thickness(0);
            this.Background = new SolidColorBrush(Colors.GreenYellow);
            this.Cursor = Cursors.Hand;
            this.CmntLinkTag = tag;
            this.currentLineNumber = currentLineNumber;
        }

        public CommentLinkTag CmntLinkTag { get; private set; }

        internal int GetLineNumber(string pathToFile, string content, bool withinSameFile)
        {
            int lineNumber = 0;
            foreach (var line in System.IO.File.ReadAllLines(pathToFile))
            {
                lineNumber++;
                if (line.Contains(content))
                {
                    if (withinSameFile && lineNumber == (this.currentLineNumber + 1))
                    {
                        continue;
                    }

                    return lineNumber;
                }
            }

            return 0;
        }

        internal void Update(CommentLinkTag dataTag)
        {
            this.CmntLinkTag = dataTag;
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        protected override async void OnClick()
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            base.OnClick();

            try
            {
                async Task<IVsTextView> OpenFileAsync(string filePath)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    VsShellUtilities.OpenDocument(
                        new ServiceProvider((Microsoft.VisualStudio.OLE.Interop.IServiceProvider)ProjectHelpers.Dte),
                        filePath,
                        Guid.Empty,
                        out _,
                        out _,
                        out _,
                        out IVsTextView viewAdapter);

                    return viewAdapter;
                }

                async System.Threading.Tasks.Task NavigateWithinFile(IVsTextView viewAdapter, string filePath, int lineNo, string searchText, bool sameFile)
                {
                    if (viewAdapter == null)
                    {
                        return;
                    }

                    if (lineNo > 0)
                    {
                        // Set the cursor at the beginning of the declaration.
                        if (viewAdapter.SetCaretPos(lineNo - 1, 0) == VSConstants.S_OK)
                        {
                            // Make sure that the text is visible.
                            viewAdapter.CenterLines(lineNo - 1, 1);
                        }
                        else
                        {
                            await StatusBarHelper.ShowMessageAsync($"'{filePath}' contains fewer than '{lineNo}' lines.");
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(searchText))
                    {
                        var foundLineNo = this.GetLineNumber(filePath, searchText, sameFile);

                        if (foundLineNo > 0)
                        {
                            ErrorHandler.ThrowOnFailure(viewAdapter.SetCaretPos(foundLineNo - 1, 0));
                            viewAdapter.CenterLines(foundLineNo - 1, 1);
                        }
                        else
                        {
                            await StatusBarHelper.ShowMessageAsync($"Could not find '{searchText}' in '{filePath}'.");
                        }
                    }
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (this.CmntLinkTag.IsRunCommand)
                {
                    if (string.IsNullOrWhiteSpace(this.CmntLinkTag.FileName))
                    {
                        await StatusBarHelper.ShowMessageAsync($"No command to run.");
                    }
                    else
                    {
                        var spaceIndex = this.CmntLinkTag.FileName.IndexOfAny(new[] { ' ', '\t' });

                        var args = string.Empty;
                        var cmd = this.CmntLinkTag.FileName;

                        if (spaceIndex > 0)
                        {
                            cmd = this.CmntLinkTag.FileName.Substring(0, spaceIndex);
                            args = this.CmntLinkTag.FileName.Substring(spaceIndex);
                        }

                        System.Diagnostics.Process.Start(cmd, args);
                    }

                    return;
                }

                var projItem = ProjectHelpers.Dte2.Solution.FindProjectItem(this.CmntLinkTag.FileName);

                if (projItem != null)
                {
                    string filePath;

                    // If an item in a solution folder
                    if (projItem.Kind == "{66A26722-8FB5-11D2-AA7E-00C04F688DDE}")
                    {
                        filePath = projItem.FileNames[1];
                    }
                    else if (projItem.Kind == "{66A2671F-8FB5-11D2-AA7E-00C04F688DDE}")
                    {
                        // A miscellaneous file (possibly something open but not in the solution)
                        filePath = this.CmntLinkTag.FileName;
                    }
                    else
                    {
                        filePath = projItem.Properties?.Item("FullPath")?.Value?.ToString();
                    }

                    if (filePath != null)
                    {
                        IVsTextView viewAdapter = null;
                        bool sameFile = false;

                        var activeDocPath = ProjectHelpers.Dte.ActiveDocument.FullName;

                        if (activeDocPath == filePath || System.IO.Path.GetFileName(activeDocPath) == filePath)
                        {
                            viewAdapter = this.GetActiveTextView();
                            filePath = activeDocPath;
                            sameFile = true;
                        }
                        else
                        {
                            viewAdapter = await OpenFileAsync(filePath);
                            sameFile = filePath == activeDocPath;
                        }

                        if (viewAdapter != null)
                        {
                            await NavigateWithinFile(viewAdapter, filePath, this.CmntLinkTag.LineNo, this.CmntLinkTag.SearchTerm, sameFile);
                        }
                        else
                        {
                            await StatusBarHelper.ShowMessageAsync($"Unable to find file '{this.CmntLinkTag.FileName}'");
                        }
                    }
                }
                else if (System.IO.File.Exists(this.CmntLinkTag.FileName))
                {
                    var va = await OpenFileAsync(this.CmntLinkTag.FileName);
                    await NavigateWithinFile(va, this.CmntLinkTag.FileName, this.CmntLinkTag.LineNo, this.CmntLinkTag.SearchTerm, sameFile: false);
                }
                else if (Uri.IsWellFormedUriString(this.CmntLinkTag.FileName, UriKind.Absolute))
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(this.CmntLinkTag.FileName)
                        { UseShellExecute = true });
                }
                else
                {
                    await StatusBarHelper.ShowMessageAsync($"Unable to find file '{this.CmntLinkTag.FileName}'");
                }
            }
            catch (Exception exc)
            {
                await OutputPane.Instance.WriteAsync(exc);
            }
        }

        // From https://docs.microsoft.com/en-us/visualstudio/extensibility/walkthrough-creating-a-view-adornment-commands-and-settings-column-guides?view=vs-2019
        /// <summary>
        /// Find the active text view (if any) in the active document.
        /// </summary>
        /// <returns>The IVsTextView of the active view, or null if there is no active
        /// document or the
        /// active view in the active document is not a text view.</returns>
        private IVsTextView GetActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsMonitorSelection selection =
                ServiceProvider.GlobalProvider.GetService(typeof(IVsMonitorSelection)) as IVsMonitorSelection;
            Assumes.Present(selection);
            ErrorHandler.ThrowOnFailure(
                selection.GetCurrentElementValue(
                    (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object frameObj));

            if (!(frameObj is IVsWindowFrame frame))
            {
                return null;
            }

            return GetActiveView(frame);
        }

        private static IVsTextView GetActiveView(IVsWindowFrame windowFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (windowFrame == null)
            {
                throw new ArgumentException("windowFrame");
            }

            ErrorHandler.ThrowOnFailure(
                windowFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object pvar));

            IVsTextView textView = pvar as IVsTextView;
            if (textView == null)
            {
                if (pvar is IVsCodeWindow codeWin)
                {
                    ErrorHandler.ThrowOnFailure(codeWin.GetLastActiveView(out textView));
                }
            }

            return textView;
        }
    }
}
