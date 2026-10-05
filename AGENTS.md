# Project delivery rules

- Save all distributable installers and formatting profiles in the repository-root `release/` directory. Keep build intermediates and diagnostic logs in `artifacts/` or the usual `bin/` and `obj/` directories.
- When preparing a release, include separate packages for the supported Visual Studio and SSMS generations, the native SQL Prompt/SQL Complete-derived JSON profiles, installation instructions, and verification results. Never claim installed-host validation from a successful build alone.
- Keep the Russian and English user manuals consistent with shipped behavior.
