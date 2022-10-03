import * as vscode from 'vscode';
import * as languageClient from "vscode-languageclient";
import * as languageClientNode from "vscode-languageclient/node";
import * as path from "path";
import * as fs from "fs";

const languageServerPath : string = "server/DenizenLangServer.dll";

let configuration : vscode.WorkspaceConfiguration = vscode.workspace.getConfiguration();

let headerSymbols : string = "|+=#_@/";

let outputChannel = vscode.window.createOutputChannel("Denizen");

let debugHighlighting : boolean = false;
let debugFolding : boolean = false;
let doInlineColors : boolean = true;

class HighlightCache {
    needRefreshStartLine : number = -1;
    needRefreshEndLine : number = -1;
    needRefreshLineShift : number = 0;
    lastDecorations : { [color: string]: vscode.Range[] } = {};
}

let HLCaches : Map<string, HighlightCache> = new Map<string, HighlightCache>();

function getCache(path : string) {
    let result : HighlightCache = HLCaches.get(path);
    if (result) {
        return result;
    }
    result = new HighlightCache();
    HLCaches.set(path, result);
    return result;
}

function activateLanguageServer(context: vscode.ExtensionContext, dotnetPath : string) {
    if (!dotnetPath || dotnetPath.length === 0) {
        dotnetPath = "dotnet";
    }
    let pathFile : string = context.asAbsolutePath(languageServerPath);
    if (!fs.existsSync(pathFile)) {
        return;
    }
    let pathDir : string = path.dirname(pathFile);
    let serverOptions: languageClientNode.ServerOptions = {
        run: { command: dotnetPath, args: [pathFile], options: { cwd: pathDir } },
        debug: { command: dotnetPath, args: [pathFile, "--debug"], options: { cwd: pathDir } }
    }
    let clientOptions: languageClient.LanguageClientOptions = {
        documentSelector: ["denizenscript"],
        synchronize: {
            configurationSection: "denizenscript",
        },
    }
    let client = new languageClientNode.LanguageClient("DenizenLangServer", "Denizen Language Server", serverOptions, clientOptions);
    let disposable = client.start();
    context.subscriptions.push(disposable);
}

const highlightDecors: { [color: string]: vscode.TextEditorDecorationType } = {};
const highlightColorRef: { [color: string]: string } = {};

function parseColor(inColor : string) : vscode.DecorationRenderOptions {
    const colorSplit : string[] = inColor.split('\|');
    let resultColor : vscode.DecorationRenderOptions = { color : colorSplit[0] };
    let strike : boolean = false;
    let underline : boolean = false;
    for (const i in colorSplit) {
        const subValueSplit = colorSplit[i].split('=', 2);
        const subValueSetting = subValueSplit[0];
        if (subValueSetting == "style") {
            resultColor.fontStyle = subValueSplit[1];
        }
        else if (subValueSetting == "weight") {
            resultColor.fontWeight = subValueSplit[1];
        }
        else if (subValueSetting == "strike") {
            strike = subValueSplit[1] == "true";
        }
        else if (subValueSetting == "underline") {
            underline = subValueSplit[1] == "true";
        }
        else if (subValueSetting == "background") {
            resultColor.backgroundColor = subValueSplit[1];
        }
    }
    if (strike || underline) {
        if (strike && !underline) {
            resultColor.textDecoration = "line-through";
        }
        else if (underline && !strike) {
            resultColor.textDecoration = "underline";
        }
        else {
            resultColor.textDecoration = "underline line-through";
        }
    }
    return resultColor;
}

function colorSet(name : string, inColor : string) {
    highlightDecors[name] = vscode.window.createTextEditorDecorationType(parseColor(inColor));
    highlightColorRef[name] = inColor;
}

const colorTypes : string[] = [
    "comment_header", "comment_normal", "comment_todo", "comment_code",
    "key", "key_inline", "command", "quote_double", "quote_single", "def_name",
    "tag", "tag_dot", "tag_param", "tag_param_bracket", "bad_space", "colons", "space", "normal"
];

function loadAllColors() {
    configuration = vscode.workspace.getConfiguration();
    for (const i in colorTypes) {
        let str : string = configuration.get("denizenscript.theme_colors." + colorTypes[i]);
        if (str === undefined) {
            outputChannel.appendLine("Missing color config for " + colorTypes[i]);
            continue;
        }
        colorSet(colorTypes[i], str);
    }
    headerSymbols = configuration.get("denizenscript.header_symbols");
    debugHighlighting = configuration.get("denizenscript.debug.highlighting");
    debugFolding = configuration.get("denizenscript.debug.folding");
    doInlineColors = configuration.get("denizenscript.behaviors.do_inline_colors");
    const customColors : string = configuration.get("denizenscript.theme_colors.text_color_map");
    const colorsSplit : string[] = customColors.split(',');
    for (const i in colorsSplit) {
        const color = colorsSplit[i];
        let pair : string[] = color.split('=');
        if (pair.length == 2) {
            tagSpecialColors["&[" + pair[0] + "]"] = pair[1];
        }
        else {
            outputChannel.appendLine("Cannot interpret color " + color);
        }
    }
}

function activateHighlighter(context: vscode.ExtensionContext) {
    loadAllColors();
}

let refreshTimer: NodeJS.Timer | undefined = undefined;

function refreshDecor() {
    refreshTimer = undefined;
    for (const editor of vscode.window.visibleTextEditors) {
        const uri = editor.document.uri.toString();
        if (!uri.endsWith(".dsc")) {
            continue;
        }
        decorateFullFile(editor);
    }
}

function addDecor(decorations: { [color: string]: vscode.Range[] }, type: string, lineNumber: number, startChar: number, endChar: number) {
    if (!(type in highlightDecors) && type.startsWith("auto:")) {
        highlightDecors[type] = vscode.window.createTextEditorDecorationType(parseColor(type.substring("auto:".length)));
        decorations[type] = [];
    }
    decorations[type].push(new vscode.Range(new vscode.Position(lineNumber, startChar), new vscode.Position(lineNumber, endChar)));
}

function decorateTag(tag : string, start: number, lineNumber: number, decorations: { [color: string]: vscode.Range[] }) {
    const len : number = tag.length;
    let inTagCounter : number = 0;
    let tagStart : number = 0;
    let inTagParamCounter : number = 0;
    let defaultDecor : string = "tag";
    let lastDecor : number = -1; // Color the < too.
    let textColor : string = "tag_param";
    for (let i = 0; i < len; i++) {
        const c : string = tag.charAt(i);
        if (c == '<') {
            inTagCounter++;
            if (inTagCounter == 1) {
                addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
                lastDecor = i;
                defaultDecor = "tag";
                tagStart = i;
            }
        }
        else if (c == '>' && inTagCounter > 0) {
            inTagCounter--;
            if (inTagCounter == 0) {
                const tagText : string = tag.substring(tagStart + 1, i);
                let autoColor : string = getTagColor(tagText, textColor);
                if (autoColor != null) {
                    addDecor(decorations, "auto:" + autoColor, lineNumber, start + tagStart + 1, start + i);
                    addDecor(decorations, "tag", lineNumber, start + tagStart, start + tagStart + 1);
                    defaultDecor = "auto:" + autoColor;
                    textColor = defaultDecor;
                }
                else {
                    decorateTag(tagText, start + tagStart + 1, lineNumber, decorations);
                    defaultDecor = inTagParamCounter > 0 ? textColor : "tag";
                }
                addDecor(decorations, "tag", lineNumber, start + i, start + i + 1);
                lastDecor = i + 1;
            }
        }
        else if (c == '[' && inTagCounter == 0 && i + 1 < len) {
            inTagParamCounter++;
            if (inTagParamCounter == 1) {
                addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
                addDecor(decorations, "tag_param_bracket", lineNumber, start + i, start + i + 1);
                lastDecor = i + 1;
                if (i == 0) {
                    defaultDecor = "def_name";
                }
                else {
                    defaultDecor = "tag_param";
                }
            }
        }
        else if (c == ']' && inTagCounter == 0) {
            inTagParamCounter--;
            if (inTagParamCounter == 0) {
                addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
                addDecor(decorations, "tag_param_bracket", lineNumber, start + i, start + i + 1);
                defaultDecor = "tag";
                lastDecor = i + 1;
            }
        }
        else if ((c == '.' || c == '|') && inTagCounter == 0 && inTagParamCounter == 0) {
            addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
            lastDecor = i + 1;
            addDecor(decorations, "tag_dot", lineNumber, start + i, start + i + 1);
        }
        else if (c == ' ' && inTagCounter == 0) {
            addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
            addDecor(decorations, "space", lineNumber, start + i, start + i + 1);
            lastDecor = i + 1;
        }
    }
    if (lastDecor < len) {
        addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + len);
    }
}

const ifOperators : string[] = [ "<", ">", "<=", ">=", "==", "!=", "||", "&&", "(", ")", "or", "not", "and", "in", "contains", "!in", "!contains", "matches", "!matches" ];

const ifCmdLabels : string[] = [ "cmd:if", "cmd:else", "cmd:while", "cmd:waituntil" ];

const deffableCmdLabels : string[] = [ "cmd:run", "cmd:runlater", "cmd:clickable", "cmd:bungeerun" ];

function checkIfHasTagEnd(arg : string, quoted: boolean, quoteMode: string, canQuote : boolean) : boolean {
    const len : number = arg.length;
    let params : number = 0;
    for (let i = 0; i < len; i++) {
        const c : string = arg.charAt(i);
        if (canQuote && (c == '"' || c == '\'')) {
            if (quoted && c == quoteMode) {
                quoted = false;
            }
            else if (!quoted) {
                quoted = true;
                quoteMode = c;
            }
        }
        else if (c == '[') {
            params++;
        }
        else if (c == ']' && params > 0) {
            params--;
        }
        else if (c == '>') {
            return true;
        }
        else if (c == ' ' && !quoted && canQuote && params == 0) {
            return false;
        }
    }
    return false;
}


const tagSpecialColors: { [color: string]: string } = {
    "&0": "#000000", "black": "#000000",
    "&1": "#0000AA", "dark_blue": "#0000AA",
    "&2": "#00AA00", "dark_green": "#00AA00",
    "&3": "#00AAAA", "dark_aqua": "#00AAAA",
    "&4": "#AA0000", "dark_red": "#AA0000",
    "&5": "#AA00AA", "dark_purple": "#AA00AA",
    "&6": "#FFAA00", "gold": "#FFAA00",
    "&7": "#AAAAAA", "gray": "#AAAAAA",
    "&8": "#555555", "dark_gray": "#555555",
    "&9": "#5555FF", "blue": "#5555FF",
    "&a": "#55FF55", "green": "#55FF55",
    "&b": "#55FFFF", "aqua": "#55FFFF",
    "&c": "#FF5555", "red": "#FF5555",
    "&d": "#FF55FF", "light_purple": "#FF55FF",
    "&e": "#FFFF55", "yellow": "#FFFF55",
    "&f": "#FFFFFF", "white": "#FFFFFF", "&r": "#FFFFFF", "reset": "#FFFFFF"
};
const formatCodes: { [code: string]: string } = {
    "&l": "bold", "&L": "bold", "bold": "bold",
    "&o": "italic", "&O": "italic", "italic": "italic",
    "&m": "strike", "&M": "strike", "strikethrough": "strike",
    "&n": "underline", "&N": "underline", "underline": "underline"
};

const hexChars: { [c: string] : boolean } = {}
const hexRefStr = "abcdefABCDEF0123456789";
for (let hexID = 0; hexID < hexRefStr.length; hexID++) {
    hexChars[hexRefStr.charAt(hexID)] = true;
}

function isHex(text : string) : boolean {
    for (let i = 0; i < text.length; i++) {
        let c : string = text.charAt(i);
        if (!(c in hexChars)) {
            return false;
        }
    }
    return true;
}

function getColorData(color : string) : string {
    if (color.startsWith("auto:#")) {
        return color.substring("auto:".length);
    }
    const knownColor : string = highlightColorRef[color];
    if (knownColor) {
        return knownColor;
    }
    return null;
}

function getTagColor(tagText : string, preColor : string) : string {
    if (!doInlineColors) {
        return null;
    }
    if (tagText in tagSpecialColors) {
        return tagSpecialColors[tagText];
    }
    if (tagText.startsWith("&color[") && tagText.endsWith("]") && !tagText.includes(".")) {
        const colorText : string = tagText.substring("&color[".length, tagText.length - 1);
        if (colorText.length == 7 && colorText.startsWith("#") && isHex(colorText.substring(1))) {
            return colorText;
        }
    }
    const formatter : string = formatCodes[tagText];
    if (formatter) {
        const rgb : string = getColorData(preColor);
        if (rgb) {
            if (formatter == "bold") {
                return rgb + "|weight=bold";
            }
            else if (formatter == "italic") {
                return rgb + "|style=italic";
            }
            else if (formatter == "strike") {
                return rgb + "|strike=true";
            }
            else if (formatter == "underline") {
                return rgb + "|underline=true";
            }
        }
    }
    return null;
}

const TAG_ALLOWED : string = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789&_[";

function decorateArg(arg : string, start: number, lineNumber: number, decorations: { [color: string]: vscode.Range[] }, canQuote : boolean, contextualLabel : string) {
    const len : number = arg.length;
    let quoted : boolean = false;
    let quoteMode : string = 'x';
    let inTagCounter : number = 0;
    let tagStart : number = 0;
    const referenceDefault = contextualLabel == "key:definitions" ? "def_name" : "normal";
    let defaultDecor : string = referenceDefault;
    let lastDecor : number = 0;
    let hasTagEnd : boolean = checkIfHasTagEnd(arg, false, 'x', canQuote);
    let spaces : number = 0;
    let textColor : string = referenceDefault;
    for (let i = 0; i < len; i++) {
        const c : string = arg.charAt(i);
        if (canQuote && (c == '"' || c == '\'')) {
            if (quoted && c == quoteMode) {
                addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i + 1);
                lastDecor = i + 1;
                defaultDecor = referenceDefault;
                textColor = defaultDecor;
                quoted = false;
            }
            else if (!quoted) {
                addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
                lastDecor = i;
                quoted = true;
                defaultDecor = c == '"' ? "quote_double" : "quote_single";
                textColor = defaultDecor;
                quoteMode = c;
            }
        }
        else if (hasTagEnd && c == '<' && i + 1 < len && TAG_ALLOWED.includes(arg.charAt(i + 1))) {
            inTagCounter++;
            if (inTagCounter == 1) {
                addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
                lastDecor = i;
                tagStart = i;
                defaultDecor = "tag";
            }
        }
        else if (hasTagEnd && c == '>' && inTagCounter > 0) {
            inTagCounter--;
            if (inTagCounter == 0) {
                const tagText : string = arg.substring(tagStart + 1, i);
                let autoColor : string = getTagColor(tagText, textColor);
                if (autoColor != null) {
                    addDecor(decorations, "tag", lineNumber, start + tagStart, start + tagStart + 1);
                    addDecor(decorations, "auto:" + autoColor, lineNumber, start + tagStart + 1, start + i);
                    defaultDecor = "auto:" + autoColor;
                    textColor = defaultDecor;
                }
                else {
                    decorateTag(tagText, start + tagStart + 1, lineNumber, decorations);
                    defaultDecor = textColor;
                }
                addDecor(decorations, "tag", lineNumber, start + i, start + i + 1);
                lastDecor = i + 1;
            }
        }
        else if (inTagCounter == 0 && c == '|' && contextualLabel == "key:definitions") {
            addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
            addDecor(decorations, "normal", lineNumber, start + i, start + i + 1);
            lastDecor = i + 1;
        }
        else if (inTagCounter == 0 && c == ':' && deffableCmdLabels.includes(contextualLabel.replace("~", ""))) {
            const part : string = arg.substring(lastDecor, i);
            if (part.startsWith("def.") && !part.includes('<') && !part.includes(' ')) {
                addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + "def.".length);
                addDecor(decorations, "def_name", lineNumber, start + lastDecor + "def.".length, start + i);
                lastDecor = i;
            }
        }
        else if (c == ' ' && !quoted && canQuote && inTagCounter == 0) {
            hasTagEnd = checkIfHasTagEnd(arg.substring(i + 1), quoted, quoteMode, canQuote);
            addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + i);
            addDecor(decorations, "space", lineNumber, start + i, start + i + 1);
            lastDecor = i + 1;
            if (!quoted) {
                inTagCounter = 0;
                defaultDecor = textColor;
                spaces++;
            }
            const nextArg : string = arg.includes(" ", i + 1) ? arg.substring(i + 1, arg.indexOf(" ", i + 1)) : arg.substring(i + 1);
            if (!quoted && canQuote) {
                if (ifOperators.includes(nextArg) && ifCmdLabels.includes(contextualLabel)) {
                    addDecor(decorations, "colons", lineNumber, start + i + 1, start + i + 1 + nextArg.length);
                    i += nextArg.length;
                    lastDecor = i;
                }
                else if (nextArg.startsWith("as:") && !nextArg.includes("<") && (contextualLabel == "cmd:foreach" || contextualLabel == "cmd:repeat")) {
                    addDecor(decorations, "normal", lineNumber, start + i + 1, start + i + 1 + "as:".length);
                    addDecor(decorations, "def_name", lineNumber, start + i + 1 + "as:".length, start + i + 1 + nextArg.length);
                    i += nextArg.length;
                    lastDecor = i;
                }
                else if (nextArg.startsWith("key:") && !nextArg.includes("<") && contextualLabel == "cmd:foreach") {
                    addDecor(decorations, "normal", lineNumber, start + i + 1, start + i + 1 + "key:".length);
                    addDecor(decorations, "def_name", lineNumber, start + i + 1 + "key:".length, start + i + 1 + nextArg.length);
                    i += nextArg.length;
                    lastDecor = i;
                }
                else if (spaces == 1 && (contextualLabel == "cmd:define" || contextualLabel == "cmd:definemap")) {
                    let colonIndex : number = nextArg.indexOf(':');
                    if (colonIndex == -1) {
                        colonIndex = nextArg.length;
                    }
                    const tagMark : number = nextArg.indexOf('<');
                    if (tagMark == -1 || tagMark > colonIndex) {
                        addDecor(decorations, "def_name", lineNumber, start + i + 1, start + i + 1 + colonIndex);
                        const argStart : string = nextArg.charAt(0);
                        if (!quoted && canQuote && (argStart == '"' || argStart == '\'')) {
                            quoted = true;
                            defaultDecor = argStart == '"' ? "quote_double" : "quote_single";
                            quoteMode = argStart;
                        }
                        i += colonIndex;
                        lastDecor = i;
                    }
                }
            }
        }
    }
    if (lastDecor < len) {
        addDecor(decorations, defaultDecor, lineNumber, start + lastDecor, start + len);
    }
}

function decorateComment(line : string, lineNumber: number, decorType: string, decorations: { [color: string]: vscode.Range[] }) {
    decorateSpaceable(line, 0, lineNumber, decorType, decorations);
}

function decorateSpaceable(line : string, preLength: number, lineNumber: number, decorType: string, decorations: { [color: string]: vscode.Range[] }) {
    const len : number = line.length;
    let lastDecor : number = 0;
    for (let i = 0; i < len; i++) {
        const c : string = line.charAt(i);
        if (c == ' ') {
            addDecor(decorations, decorType, lineNumber, preLength + lastDecor, preLength + i);
            addDecor(decorations, "space", lineNumber, preLength + i, preLength + i + 1);
            lastDecor = i + 1;
        }
    }
    if (lastDecor < len) {
        addDecor(decorations, decorType, lineNumber, preLength + lastDecor, preLength + len);
    }
}

const definiteNotScriptKeys : string[] = [
    "interact scripts", "default constants", "data", "constants", "text", "lore", "aliases", "slots", "enchantments", "input"
];

function decorateLine(line : string, lineNumber: number, decorations: { [color: string]: vscode.Range[] }, lastKey : string, isData : boolean) {
    if (line.endsWith("\r")) {
        line = line.substring(0, line.length - 1);
    }
    const trimmedEnd : string = line.trimRight();
    let trimmed : string = trimmedEnd.trimLeft();
    if (trimmed.length == 0) {
        return;
    }
    if (trimmedEnd.length != line.length) {
        addDecor(decorations, "bad_space", lineNumber, trimmedEnd.length, line.length);
    }
    const preSpaces = trimmedEnd.length - trimmed.length;
    if (trimmed.startsWith("#")) {
        const afterComment = trimmed.substring(1).trim();
        const symbol = afterComment.length == 0 ? ' ' : afterComment.charAt(0);
        if (headerSymbols.includes(symbol)) {
            decorateComment(line, lineNumber, "comment_header", decorations);
        }
        else if (afterComment.startsWith("-")) {
            decorateComment(line, lineNumber, "comment_code", decorations);
        }
        else if (afterComment.toLowerCase().startsWith("todo")) {
            decorateComment(line, lineNumber, "comment_todo", decorations);
        }
        else {
            decorateComment(line, lineNumber, "comment_normal", decorations);
        }
    }
    else if (trimmed.startsWith("-")) {
        const isNonScript : boolean = isData || definiteNotScriptKeys.includes(lastKey);
        addDecor(decorations, "normal", lineNumber, preSpaces, preSpaces + 1);
        if (isNonScript) {
            decorateArg(trimmed.substring(1), preSpaces + 1, lineNumber, decorations, false, "non-script");
        }
        else {
            if (trimmed.endsWith(":")) {
                addDecor(decorations, "colons", lineNumber, preSpaces + trimmed.length - 1, preSpaces + trimmed.length);
                trimmed = trimmed.substring(0, trimmed.length - 1);
            }
            const afterDash : string = trimmed.substring(1);
            const commandEnd : number = afterDash.indexOf(' ', 1) + 1;
            const endIndexCleaned : number = preSpaces + (commandEnd == 0 ? trimmed.length : commandEnd);
            const commandText = commandEnd == 0 ? afterDash : afterDash.substring(0, commandEnd);
            if (!afterDash.startsWith(" ")) {
                addDecor(decorations, "bad_space", lineNumber, preSpaces + 1, endIndexCleaned);
                decorateArg(trimmed.substring(commandEnd), preSpaces + commandEnd, lineNumber, decorations, false, "cmd:" + commandText.trim());
            }
            else {
                if (commandText.includes("'") || commandText.includes("\"") || commandText.includes("[")) {
                    decorateArg(trimmed.substring(2), preSpaces + 2, lineNumber, decorations, false, "non-cmd");
                }
                else {
                    addDecor(decorations, "command", lineNumber, preSpaces + 2, endIndexCleaned);
                    if (commandEnd > 0) {
                        decorateArg(trimmed.substring(commandEnd), preSpaces + commandEnd, lineNumber, decorations, true, "cmd:" + commandText.trim());
                    }
                }
            }
        }
    }
    else if (trimmed.endsWith(":")) {
        decorateSpaceable(trimmed.substring(0, trimmed.length - 1), preSpaces, lineNumber, "key", decorations);
        addDecor(decorations, "colons", lineNumber, trimmedEnd.length - 1, trimmedEnd.length);
    }
    else if (trimmed.includes(":")) {
        const colonIndex = line.indexOf(':');
        const key = trimmed.substring(0, colonIndex - preSpaces);
        decorateSpaceable(key, preSpaces, lineNumber, "key", decorations);
        addDecor(decorations, "colons", lineNumber, colonIndex, colonIndex + 1);
        decorateArg(trimmed.substring(colonIndex - preSpaces + 1), colonIndex + 1, lineNumber, decorations, false, "key:" + key);
    }
    else {
        addDecor(decorations, "bad_space", lineNumber, preSpaces, line.length);
    }
}

function decorateFullFile(editor: vscode.TextEditor) {
    let decorations: { [color: string]: vscode.Range[] } = {};
    let highlight : HighlightCache = getCache(editor.document.uri.toString());
    if (Object.keys(highlight.lastDecorations).length === 0) {
        highlight.needRefreshStartLine = -1;
    }
    if (highlight.needRefreshStartLine == -1) {
        for (const c in highlightDecors) {
            decorations[c] = [];
        }
    }
    else {
        if (highlight.needRefreshLineShift > 0) {
            highlight.needRefreshEndLine += highlight.needRefreshLineShift;
        }
        if (highlight.needRefreshLineShift < 0) {
            highlight.needRefreshStartLine += highlight.needRefreshLineShift;
        }
        decorations = highlight.lastDecorations;
        for (const c in highlightDecors) {
            const rangeSet : vscode.Range[] = decorations[c];
            if (highlight.needRefreshLineShift != 0) {
                for (let i : number = rangeSet.length - 1; i >= 0; i--) {
                    if (highlight.needRefreshLineShift > 0 ? (rangeSet[i].start.line >= highlight.needRefreshEndLine - highlight.needRefreshLineShift) : (rangeSet[i].start.line >= highlight.needRefreshStartLine - highlight.needRefreshLineShift)) {
                        rangeSet[i] = new vscode.Range(new vscode.Position(rangeSet[i].start.line + highlight.needRefreshLineShift, rangeSet[i].start.character), new vscode.Position(rangeSet[i].end.line + highlight.needRefreshLineShift, rangeSet[i].end.character));
                    }
                }
            }
            for (let i : number = rangeSet.length - 1; i >= 0; i--) {
                if (rangeSet[i].start.line <= highlight.needRefreshEndLine && rangeSet[i].end.line >= highlight.needRefreshStartLine) {
                    rangeSet.splice(i, 1);
                }
            }
        }
    }
    const fullText : string = editor.document.getText();
    const splitText : string[] = fullText.split('\n');
    const totalLines = splitText.length;
    let lastKey : string = "";
    const startLine : number = (highlight.needRefreshStartLine == -1 ? 0 : highlight.needRefreshStartLine);
    const endLine : number = (highlight.needRefreshStartLine == -1 ? totalLines : Math.min(highlight.needRefreshEndLine + 1, totalLines));
    if (debugHighlighting) {
        if (highlight.needRefreshStartLine == -1) {
            let type : String = "normal";
            if (highlight.needRefreshEndLine == 999999) {
                type = "forced";
            }
            else if (Object.keys(highlight.lastDecorations).length === 0) {
                type = "missing-keys-induced";
            }
            outputChannel.appendLine("Doing " + type + " full highlight of entire file, for file: " + editor.document.fileName);
        }
        else {
            outputChannel.appendLine("Doing partial highlight of file from start " + startLine + " to end " + endLine + ", for file: " + editor.document.fileName);
        }
    }
    let definitelyDataSpacing : number = -1;
    // Actually choose colors
    for (let i : number = 0; i < endLine; i++) {
        const lineText : string = splitText[i];
        const trimmedLineStart : string = lineText.trimStart();
        const spaces : number = lineText.length - trimmedLineStart.length;
        const trimmedLine : string = trimmedLineStart.trimEnd();
        if (trimmedLine.endsWith(":") && !trimmedLine.startsWith("-")) {
            lastKey = trimmedLine.substring(0, trimmedLine.length - 1).toLowerCase();
            if (spaces <= definitelyDataSpacing) {
                definitelyDataSpacing = -1;
            }
            if (definiteNotScriptKeys.includes(lastKey) && definitelyDataSpacing == -1) {
                definitelyDataSpacing = spaces;
            }
        }
        else if (trimmedLine == "type: data" && (definitelyDataSpacing == -1 || spaces <= definitelyDataSpacing)) {
            definitelyDataSpacing = spaces - 1;
        }
        if (i >= startLine) {
            decorateLine(lineText, i, decorations, lastKey, definitelyDataSpacing != -1);
        }
    }
    // Apply them
    for (const c in decorations) {
        editor.setDecorations(highlightDecors[c], decorations[c]);
    }
    highlight.lastDecorations = decorations;
    highlight.needRefreshStartLine = -1;
    highlight.needRefreshEndLine = -1;
    highlight.needRefreshLineShift = 0;
}

function denizenScriptFoldingProvider(document: vscode.TextDocument, context: vscode.FoldingContext, token: vscode.CancellationToken) : vscode.ProviderResult<vscode.FoldingRange[]> {
    const fullText : string = document.getText();
    const splitText : string[] = fullText.split('\n');
    const totalLines = splitText.length;
    const output : vscode.FoldingRange[] = [];
    const processing : InProcFold[] = [];
    if (debugFolding) {
        outputChannel.appendLine("(FOLDING) Begin");
    }
    for (let i : number = 0; i < totalLines; i++) {
        const line : string = splitText[i];
        const preTrimmed : string = line.trimStart();
        if (preTrimmed.length == 0) {
            continue;
        }
        const spaces : number = line.length - preTrimmed.length;
        const fullTrimmed : string = preTrimmed.trimEnd();
        const isBlock : boolean = fullTrimmed.endsWith(":");
        const isCommand : boolean = fullTrimmed.startsWith("-");
        while (processing.length > 0) {
            const lastFold : InProcFold = processing[processing.length - 1];
            if (lastFold.spacing > spaces || spaces == 0 || (lastFold.spacing == spaces && ((isBlock && !isCommand) || lastFold.isCommand))) {
                processing.pop();
                output.push(new vscode.FoldingRange(lastFold.start, i - 1));
                if (debugFolding) {
                    outputChannel.appendLine("(FOLDING) Found an end at " + i);
                }
            }
            else {
                break;
            }
        }
        if (isBlock) {
            processing.push(new InProcFold(i, spaces, isCommand));
            if (debugFolding) {
                outputChannel.appendLine("(FOLDING) Found a start at " + i);
            }
        }
    }
    if (debugFolding) {
        outputChannel.appendLine("(FOLDING) Folds calculated with " + output.length + " normal and " + processing.length + " left");
    }
    for (let i : number = 0; i < processing.length; i++) { // for-each style loop bugs out and thinks the value is a String, so have to do 'i' counter style loop
        const extraFold : InProcFold = processing[i];
        output.push(new vscode.FoldingRange(extraFold.start, totalLines - 1));
    }
    return output;
}

function scheduleRefresh() {
    if (refreshTimer) {
        return;
    }
    refreshTimer = setTimeout(refreshDecor, 50);
}

async function activateDotNet() {
    try {
        outputChannel.appendLine("DenizenScript extension attempting to acquire .NET 6");
        const requestingExtensionId = 'DenizenScript.denizenscript';
        const result = await vscode.commands.executeCommand('dotnet.acquire', { version: '6.0', requestingExtensionId });
        outputChannel.appendLine("DenizenScript extension NET 6 Acquire result: " + result + ": " + result["dotnetPath"]);
        return result["dotnetPath"];
    }
    catch (error) {
        outputChannel.appendLine("Error: " + error);
        return "";
    }
}

function forceRefresh(reason: String) {
    if (debugHighlighting) {
        outputChannel.appendLine("Scheduled a force full refresh of syntax highlighting because: " + reason);
    }
    HLCaches.clear();
    scheduleRefresh();
}

let changeCounter : number = 0;

export async function activate(context: vscode.ExtensionContext) {
    let path : string = await activateDotNet();
    activateLanguageServer(context, path);
    activateHighlighter(context);
    vscode.workspace.onDidOpenTextDocument(doc => {
        if (doc.uri.toString().endsWith(".dsc")) {
            forceRefresh("onDidOpenTextDocument");
        }
    }, null, context.subscriptions);
    vscode.workspace.onDidChangeTextDocument(event => {
        const curFile : string = event.document.uri.toString();
        if (curFile.endsWith(".dsc")) {
            let highlight : HighlightCache = getCache(curFile);
            event.contentChanges.forEach(change => {
                if (highlight.needRefreshStartLine == -1 || change.range.start.line < highlight.needRefreshStartLine) {
                    highlight.needRefreshStartLine = change.range.start.line;
                }
                if (highlight.needRefreshEndLine == -1 || change.range.end.line > highlight.needRefreshEndLine) {
                    highlight.needRefreshEndLine = change.range.end.line;
                }
                highlight.needRefreshLineShift += change.text.split('\n').length - 1;
                highlight.needRefreshLineShift -= event.document.getText(change.range).split('\n').length - 1;
            });
            if (debugHighlighting) {
                outputChannel.appendLine("Scheduled a partial refresh of syntax highlighting because onDidChangeTextDocument, from " + highlight.needRefreshStartLine + " to " + highlight.needRefreshEndLine + " with shift " + highlight.needRefreshLineShift);
            }
            scheduleRefresh();
            if (changeCounter++ < 2) {
                forceRefresh("onDidChangeTextDocument" + changeCounter);
            }
        }
    }, null, context.subscriptions);
    vscode.window.onDidChangeVisibleTextEditors(editors => {
        forceRefresh("onDidChangeVisibleTextEditors");
    }, null, context.subscriptions);
    vscode.workspace.onDidChangeConfiguration(event => {
        loadAllColors();
        forceRefresh("onDidChangeConfiguration");
    });
    vscode.languages.registerFoldingRangeProvider('denizenscript', {
        provideFoldingRanges(document: vscode.TextDocument, context: vscode.FoldingContext, token: vscode.CancellationToken) : vscode.ProviderResult<vscode.FoldingRange[]> {
            return denizenScriptFoldingProvider(document, context, token);
        }
    });
    scheduleRefresh();
    outputChannel.appendLine('Denizen extension has been activated');
}

class InProcFold {
    start : number;
    spacing : number;
    isCommand : boolean;
    constructor(start: number, spacing: number, isCommand : boolean) {
        this.start = start;
        this.spacing = spacing;
        this.isCommand = isCommand;
    }
}

export function deactivate() {
}
