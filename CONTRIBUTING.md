# How to contribute

This repository is a fork of
[google-gemma/gemma-translator](https://github.com/google-gemma/gemma-translator).
It moves the software from Python and React to C# on .NET with Avalonia UI.

## CAUTION: this is not the upstream procedure

The upstream project belongs to Google LLC. It has a Contributor License
Agreement (CLA) and the Google open-source community guidelines. **This fork
has no CLA and no such guidelines.** Do not sign a Google CLA for a change to
this repository. A change here does not go to the upstream project.

If your change is for the Python software or for the React user interface, send
it to the upstream project and obey the procedure of that project.

## The licence of your contribution

This repository stays under the Apache License, Version 2.0. See `LICENSE`.

You keep the copyright of your work. When you send a pull request, you agree
that your work goes into this repository under Apache 2.0. Add your name to the
copyright lines of each file that you make.

Each source file gets this header:

```csharp
// Copyright 2026 Google LLC
// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// ... (the full Apache 2.0 text)
//
// This file is part of a fork of google-gemma/gemma-translator and has
// been modified.
```

The Google line stays because this fork is a derivative work of the upstream
project. Section 4(c) of the licence makes this necessary. See `NOTICE`.

## Before you write the code

Read `CLAUDE.md`. It holds the rules for all work here, and it is not a
document for information only. These points give the most trouble to a new
person:

- All prose obeys ASD-STE100 Simplified Technical English. This applies to a
  comment, a commit message, and the text of a pull request.
- This repository has **no test project**, and that is a decision. Use
  `dotnet build` and then operate the software.
- C# code **replaces** upstream code in the same change. It does not go on top
  of it.
- Use the `avalonia-docs` server before you write Avalonia code.

## The procedure

1. Make your branch from `feat/dotnet-fork`, and not from `main`.
2. Write the code, and remove the upstream code that it replaces.
3. `dotnet build` must give no error and no warning.
4. Operate the software and make sure that your change does what you say.
5. Send a pull request to `feat/dotnet-fork`.

`main` is a mirror of the upstream project. Do not commit your work to it.
