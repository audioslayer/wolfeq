# Third-Party Notices

## DevicePEQ

WolfEQ's community device profiles and FiiO protocol compatibility research reference portions of the DevicePEQ project:

- Project: https://github.com/jeromeof/devicePEQ
- Copyright: Copyright 2024 Pragmatic Audio
- License: Zero-Clause BSD (0BSD)

The following license applies to DevicePEQ:

> Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted.
>
> THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

DevicePEQ-derived configurations remain marked experimental until verified through WolfEQ on physical hardware. WolfEQ retains its own MIT license.


## NAudio 2.2.1

Playback monitoring uses NAudio.Wasapi and NAudio.Core by Mark Heath.
Source: https://github.com/naudio/NAudio/tree/v2.2.1
License: MIT.

Copyright 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.


## Equalizer APO 1.4.2

WolfEQ's Windows System EQ mode uses Equalizer APO as its audio engine. The WolfEQ installer and portable ZIP include the official Equalizer APO x64 installer, unmodified, at `redist\EqualizerAPO\EqualizerAPO-x64-1.4.2.exe`.

- Project: https://sourceforge.net/projects/equalizerapo/
- Copyright: Copyright Jonas Thedering
- License: GNU General Public License, version 2 (GPL-2.0). Full text: https://www.gnu.org/licenses/old-licenses/gpl-2.0.html
- Bundled file: `EqualizerAPO-x64-1.4.2.exe`, SHA-256 `7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa`, from https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-x64-1.4.2.exe/download
- Corresponding source: `EqualizerAPO-src-1.4.2.zip` at https://sourceforge.net/projects/equalizerapo/files/1.4.2/

Equalizer APO is a separate program distributed alongside WolfEQ (mere aggregation). WolfEQ does not link to, embed, or modify it: WolfEQ only starts its installer and device selector at the user's request and writes plain-text configuration files that Equalizer APO reads. WolfEQ's MIT license does not apply to Equalizer APO, and the GPL-2.0 does not apply to WolfEQ. Equalizer APO is provided by its author without warranty; see the GPL-2.0 for details.
