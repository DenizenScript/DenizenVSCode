import * as vscode from 'vscode';

let timeout : NodeJS.Timer = undefined;

export function activate(context: vscode.ExtensionContext) {

    let xCurrentTheme : string = vscode.workspace.getConfiguration().get("workbench.colorTheme");
    if (xCurrentTheme == "denizenscript") {
        timeout = setTimeout(() => {
            vscode.workspace.getConfiguration().update("workbench.colorTheme", undefined);
        }, 500);
    }
    
    const visibleEditorsDisposable = vscode.window.onDidChangeVisibleTextEditors((e) => {
        if (timeout) {
            clearTimeout(timeout);
        }
        timeout = setTimeout(() => {
            let xOrigString : string = vscode.workspace.getConfiguration().get("workbench.colorTheme");
            if (xOrigString != "denizenscript" && vscode.window.activeTextEditor.document.languageId == "denizenscript") {
                vscode.workspace.getConfiguration().update("workbench.colorTheme", "denizenscript");
            }
            else if (xOrigString == "denizenscript") {
                vscode.workspace.getConfiguration().update("workbench.colorTheme", undefined);
            }
            timeout = undefined;
        }, 500);
    });
    context.subscriptions.push(visibleEditorsDisposable);
}

export function deactivate() {}
