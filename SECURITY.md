# Security policy

## Supported versions

Only the latest release gets security fixes. A fix ships as the next version, through the app's
own update check.

| Version | Supported |
|---|---|
| 0.2.x (latest) | yes |
| older | no |

## Reporting a vulnerability

Report it privately through GitHub: open the repository's **Security** tab and choose
**Report a vulnerability** ([direct link](https://github.com/rstrlnkv/MagicKeys/security/advisories/new)).
Please do not open a public issue for a security problem.

Say what is affected, how to reproduce it and which version you ran. You get an answer in the
advisory thread; the fix and the advisory are published together.

## Scope

Of most interest: the keyboard hook, the update check and installer signature check, and the
download, unpacking and installation of Apple's driver and of 7-Zip.
