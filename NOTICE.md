# Notice

MagicKeys is licensed under the GNU General Public License v3 or later (see [LICENSE](LICENSE)).
The tree also carries the third-party material below.

## Unicode CLDR keyboard data

- **What:** the character tables of Apple's macOS keyboard layouts, used for the 33 Apple layouts.
- **Where:** `layouts/*.xml`. Each file's header comment names its CLDR source file.
- **Source:** [Unicode CLDR](https://github.com/unicode-org/cldr), release 43, folder `keyboards/osx`.
- **Copyright:** © 1991-2023 Unicode, Inc.
- **Licence:** Unicode, Inc. License Agreement — Data Files and Software; the text follows.
- **Changes:** converted from CLDR's XML into MagicKeys' own `<AppleLayout>` format; each key (named by its CLDR ISO position) got its
  PS/2 scan code and a `diff` attribute marking the levels that differ from
  Microsoft's layout for the same language. Dead-key sequences were kept as `<Dead>` entries.

### Licence text

```
UNICODE, INC. LICENSE AGREEMENT - DATA FILES AND SOFTWARE

See Terms of Use <https://www.unicode.org/copyright.html>
for definitions of Unicode Inc.’s Data Files and Software.

NOTICE TO USER: Carefully read the following legal agreement.
BY DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING UNICODE INC.'S
DATA FILES ("DATA FILES"), AND/OR SOFTWARE ("SOFTWARE"),
YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT.
IF YOU DO NOT AGREE, DO NOT DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE
THE DATA FILES OR SOFTWARE.

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2023 Unicode, Inc. All rights reserved.
Distributed under the Terms of Use in https://www.unicode.org/copyright.html.

Permission is hereby granted, free of charge, to any person obtaining
a copy of the Unicode data files and any associated documentation
(the "Data Files") or Unicode software and any associated documentation
(the "Software") to deal in the Data Files or Software
without restriction, including without limitation the rights to use,
copy, modify, merge, publish, distribute, and/or sell copies of
the Data Files or Software, and to permit persons to whom the Data Files
or Software are furnished to do so, provided that either
(a) this copyright and permission notice appear with all copies
of the Data Files or Software, or
(b) this copyright and permission notice appear in associated
Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF
ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT OF THIRD PARTY RIGHTS.
IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS
NOTICE BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL
DAMAGES, OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE,
DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER
TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THE DATA FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder
shall not be used in advertising or otherwise to promote the sale,
use or other dealings in these Data Files or Software without prior
written authorization of the copyright holder.
```

## Not in the tree

Apple's Boot Camp driver files are non-free and are never bundled; the app downloads them from Apple on
the user's machine. WiX and 7-Zip are fetched at build time or run time, never committed.
