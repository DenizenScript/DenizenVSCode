import * as vscode from 'vscode';
import * as languageClient from "vscode-languageclient";
import * as path from "path";
import * as fs from "fs";

const languageServerPath : string = "server/DenizenLangServer.dll";

function activateLanguageServer(context: vscode.ExtensionContext) {
    let pathFile : string = context.asAbsolutePath(languageServerPath);
    if (!fs.existsSync(pathFile)) {
        return;
    }
    let pathDir : string = path.dirname(pathFile);
    let serverOptions: languageClient.ServerOptions = {
        run: { command: "dotnet", args: [pathFile], options: { cwd: pathDir } },
        debug: { command: "dotnet", args: [pathFile, "--debug"], options: { cwd: pathDir } }
    }
    let clientOptions: languageClient.LanguageClientOptions = {
        documentSelector: ["denizenscript"],
        synchronize: {
            configurationSection: "DenizenLangServer",
        },
    }
    let client = new languageClient.LanguageClient("DenizenLangServer", "Denizen Language Server", serverOptions, clientOptions);
    let disposable = client.start();
    context.subscriptions.push(disposable);
}

const highlightDecors: { [color: string]: vscode.TextEditorDecorationType } = {};

function colorSet(name : string, internalName : string) {
    highlightDecors[name] = vscode.window.createTextEditorDecorationType({ color: new vscode.ThemeColor(internalName) });
}

const tagParamBackgroundColor : vscode.TextEditorDecorationType =
    vscode.window.createTextEditorDecorationType({ color: new vscode.ThemeColor("terminal.ansiMagenta") });

function activateHighlighter(context: vscode.ExtensionContext) {
    // Just colors
    colorSet("comment_header", "terminal.ansiBrightRed");
    colorSet("comment_normal", "terminal.ansiGreen");
    colorSet("comment_code", "terminal.ansiYellow");
    colorSet("key", "terminal.ansiBrightBlue");
    colorSet("quote_double", "terminal.ansiCyan");
    colorSet("quote_single", "terminal.ansiBrightCyan");
    colorSet("tag", "terminal.ansiWhite");
    colorSet("tag_dot", "terminal.ansiBrightWhite");
    // Have other formatting applied
    colorSet("command", "terminal.ansiBrightMagenta");
    colorSet("tag_param", "terminal.ansiWhite");
}

let refreshTimer: NodeJS.Timer | undefined = undefined;

function refreshDecor() {
    console.log('Denizen extension refreshing');
    refreshTimer = undefined;
    for (const editor of vscode.window.visibleTextEditors) {
        const uri = editor.document.uri.toString();
        if (!uri.endsWith(".dsc")) {
            continue;
        }
        decorate(editor);
        console.log('Denizen extension refresh: ' + uri);
    }
}

const linearDecorationTerms: string[] = [
    "comment_header", "comment_normal", "comment_code",
    "key", "quote_double", "quote_single", "tag", "tag_dot"
];
const specialDecorationTerms: string[] = [
    "command", "tag_param"
];

function commandDecoration(inRange : vscode.Range) : vscode.DecorationOptions {
    return { range: inRange, renderOptions : { before: { fontStyle: "italic" } } };
}

function tagParamDecoration(inRange : vscode.Range) : vscode.DecorationOptions {
    return { range: inRange, renderOptions : { before: { backgroundColor: tagParamBackgroundColor } } };
}

function decorate(editor: vscode.TextEditor) {
    let linearDecorations: { [color: string]: vscode.Range[] } = {};
    for (const c in linearDecorationTerms) {
        linearDecorations[linearDecorationTerms[c]] = [];
    }
    let specialDecorations: { [color: string]: vscode.DecorationOptions[] } = {};
    for (const c in specialDecorationTerms) {
        specialDecorations[specialDecorationTerms[c]] = [];
    }
    const fullText : string = editor.document.getText();
    const len : number = fullText.length;
    let line : number = 0;
    let chr : number = 0;
    for (let i : number = 0; i < len; i++) {
        let c : string = fullText.charAt(i);
        if (c == '\n') {
            line++;
            chr = 0;
            continue;
        }
        if (c == '"') {
            linearDecorations["quote_double"].push(new vscode.Range(new vscode.Position(line, chr - 1), new vscode.Position(line, chr + 1)));
        }
        if (c == '\'') {
            specialDecorations["command"].push(commandDecoration(new vscode.Range(new vscode.Position(line, chr - 1), new vscode.Position(line, chr + 1))));
        }
        chr++;
    }
    for (const c in linearDecorations) {
        editor.setDecorations(highlightDecors[c], linearDecorations[c]);
    }
    for (const c in specialDecorations) {
        editor.setDecorations(highlightDecors[c], specialDecorations[c]);
    }
}

function scheduleRefresh() {
    if (refreshTimer) {
        return;
    }
    refreshTimer = setTimeout(refreshDecor, 50);
}

export function activate(context: vscode.ExtensionContext) {
    activateLanguageServer(context);
    activateHighlighter(context);
    vscode.workspace.onDidOpenTextDocument(doc => {
        if (doc.uri.toString().endsWith(".dsc")) {
            scheduleRefresh();
        }
    }, null, context.subscriptions);
    vscode.workspace.onDidChangeTextDocument(event => {
        if (event.document.uri.toString().endsWith(".dsc")) {
            scheduleRefresh();
        }
    }, null, context.subscriptions);
    vscode.window.onDidChangeVisibleTextEditors(editors => {
        scheduleRefresh();
    }, null, context.subscriptions);
    console.log('Denizen extension has been activated');
}

export function deactivate() {
}
