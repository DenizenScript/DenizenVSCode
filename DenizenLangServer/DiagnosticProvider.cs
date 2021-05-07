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

        public async void LintCheckLoopThread()
        {
            int loops = 59;
            while (!CancelToken.IsCancellationRequested)
            {
                bool needsUpdate;
                lock (DiagUpdateLock)
                {
                    needsUpdate = NeedsNewDiag;
                    NeedsNewDiag = false;
                    loops++;
                    if (loops > 60 && DiagDoc != null && DiagDoc.Uri.AbsolutePath.EndsWith(".dsc"))
                    {
                        loops = 0;
                        needsUpdate = true;
                    }
                }
                if (needsUpdate)
                {
                    Console.Error.WriteLine("Linting...");
                    CancellationToken timeout = new CancellationTokenSource(new TimeSpan(hours: 0, minutes: 0, seconds: 10)).Token;
                    Task.Factory.StartNew(() => DoDiag(DiagSession, DiagDoc), timeout).Wait(timeout);
                }
                await Task.Delay(new TimeSpan(hours: 0, minutes: 0, seconds: 1), CancelToken);
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
            if (warning.Line < 0 || warning.StartChar < 0 || warning.EndChar < warning.StartChar)
            {
                Console.Error.WriteLine($"Error handling error: {warning.WarningUniqueKey} '{warning.CustomMessageForm}' invalid range: is on line {warning.Line} from {warning.StartChar} to {warning.EndChar}.");
                warning.Line = Math.Max(0, warning.Line);
                warning.StartChar = Math.Max(0, warning.StartChar);
                warning.EndChar = Math.Max(0, warning.EndChar);
            }
            return new Range(warning.Line, warning.StartChar, warning.Line, warning.EndChar);
        }

        public ICollection<Diagnostic> LintDocument(TextDocument document)
        {
            var diag = new List<Diagnostic>();
            ScriptChecker checker = new ScriptChecker(document.Content);
            try
            {
                checker.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            foreach (ScriptChecker.ScriptWarning warning in checker.Errors)
            {
                diag.Add(new Diagnostic(DiagnosticSeverity.Error, GetRange(warning), "Denizen Script Checker", $"(Error: {warning.WarningUniqueKey}) {warning.CustomMessageForm}"));
            }
            foreach (ScriptChecker.ScriptWarning warning in checker.Warnings)
            {
                diag.Add(new Diagnostic(DiagnosticSeverity.Error, GetRange(warning), "Denizen Script Checker", $"(Likely Error: {warning.WarningUniqueKey}) {warning.CustomMessageForm}"));
            }
            foreach (ScriptChecker.ScriptWarning warning in checker.MinorWarnings)
            {
                diag.Add(new Diagnostic(DiagnosticSeverity.Warning, GetRange(warning), "Denizen Script Checker", $"(Warning: {warning.WarningUniqueKey}) {warning.CustomMessageForm}"));
            }
            return diag;
        }
    }
}
