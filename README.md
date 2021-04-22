DenizenScript VS Code Extension
-------------------------------

An extension to [VS Code](https://github.com/microsoft/vscode) to provide general language support for `.dsc` files written for [Denizen](https://github.com/DenizenScript/Denizen).

This can be downloaded from [The Marketplace](https://marketplace.visualstudio.com/items?itemName=DenizenScript.denizenscript).

### Installation and Usage

Install guide can be found [in the Guides here](https://guide.denizenscript.com/guides/first-steps/script-editor.html).

### Building

- Within `DenizenLangServer/`, build the C# language server project with `dotnet build --configuration=release`
- Copy the output files (deepest folder under `DenizenLangServer/bin/`) to path `extension/server/`
- Within `extension/`, build the extension TypeScript files with `tsc -p ./ --skipLibCheck`

### Current Features

- Full syntax highlighting (customizable via settings)
- Error checking (imperfect but catches a lot of common mistakes)
- Some autocomplete suggestions

### Planned Future Features

- More detailed/thorough autocomplete suggestions
- More detailed error checking, including full-workspace-analysis

## Used Projects

The folder path `DenizenLangServer/LanguageServer.VsCode` is copied from https://github.com/CXuesong/LanguageServer.NET and is subject to the Apache license contained there.
Some compatibility modifications have been made.

### Licensing pre-note:

This is an open source project, provided entirely freely, for everyone to use and contribute to.

If you make any changes that could benefit the community as a whole, please contribute upstream.

### The short of the license is:

You can do basically whatever you want, except you may not hold any developer liable for what you do with the software.

### The long version of the license follows:

The MIT License (MIT)

Copyright (c) 2019-2021 The Denizen Scripting Team

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
