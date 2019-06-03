using System;
using System.Collections.Generic;
using System.Text;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Server;

using Range = LanguageServer.VsCode.Contracts.Range;

namespace DenizenLangServer
{
    public class DiagnosticProvider
    {
        public ICollection<Diagnostic> LintDocument(TextDocument document)
        {
            var diag = new List<Diagnostic>();
            return diag;
        }
    }
}
