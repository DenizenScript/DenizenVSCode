using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FreneticUtilities.FreneticToolkit;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Server;
using SharpDenizenTools.ScriptAnalysis;

using Range = LanguageServer.VsCode.Contracts.Range;

namespace DenizenLangServer
{
    public class DiagnosticProvider
    {
        public volatile bool NeedsNewDiag = false;

        public TextDocument DiagDoc = null;

        public LanguageServerSession DiagSession = null;

        public CancellationToken CancelToken = new CancellationToken();

        public LockObject DiagUpdateLock = new LockObject();

        public void InformNeedsUpdate(TextDocument document)
        {
            Task.Factory.StartNew(() =>
            {
                lock (DiagUpdateLock)
                {
                    DiagDoc = document;
                    NeedsNewDiag = true;
                }
            });
        }

        public void LintCheckLoopThread()
        {
            while (!CancelToken.IsCancellationRequested)
            {
                lock (DiagUpdateLock)
                {
                    if (NeedsNewDiag)
                    {
                        CancellationToken timeout = new CancellationTokenSource(new TimeSpan(hours: 0, minutes: 0, seconds: 10)).Token;
                        Task.Factory.StartNew(() => DoDiag(DiagSession, DiagDoc), timeout).Wait(timeout);
                        NeedsNewDiag = false;
                    }
                }
                Task.Delay(new TimeSpan(hours: 0, minutes: 0, seconds: 1), CancelToken).Wait();
            }
        }

        public void DoDiag(LanguageServerSession session, TextDocument document)
        {
            try
            {
                ICollection<Diagnostic> diagList = LintDocument(document);
                if (session.Documents.ContainsKey(document.Uri))
                {
                    Task.Factory.StartNew(() =>
                    {
                        session.Client.Document.PublishDiagnostics(document.Uri, diagList).Wait();
                    });
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }

        public Range GetRange(ScriptChecker.ScriptWarning warning)
        {
            return new Range(warning.Line, warning.StartChar, warning.Line, warning.EndChar);
        }

        public ICollection<Diagnostic> LintDocument(TextDocument document)
        {
            var diag = new List<Diagnostic>();
            ScriptChecker checker = new ScriptChecker(document.Content);
            checker.Run();
            foreach (ScriptChecker.ScriptWarning warning in checker.Errors)
            {
                diag.Add(new Diagnostic(DiagnosticSeverity.Error, GetRange(warning), "Denizen Script Checker", "(Error) " + warning.CustomMessageForm));
            }
            foreach (ScriptChecker.ScriptWarning warning in checker.Warnings)
            {
                if (warning.WarningUniqueKey == "event_missing")
                {
                    diag.Add(new Diagnostic(DiagnosticSeverity.Hint, GetRange(warning), "Denizen Script Checker", "(Imperfect!!! Event Checking. Likely ignorable) " + warning.CustomMessageForm));
                }
                else
                {
                    diag.Add(new Diagnostic(DiagnosticSeverity.Error, GetRange(warning), "Denizen Script Checker", "(Likely Error) " + warning.CustomMessageForm));
                }
            }
            foreach (ScriptChecker.ScriptWarning warning in checker.MinorWarnings)
            {
                diag.Add(new Diagnostic(DiagnosticSeverity.Warning, GetRange(warning), "Denizen Script Checker", "(Warning) " + warning.CustomMessageForm));
            }
            return diag;
        }
    }
}
