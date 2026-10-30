# Contributing

## Reporting a conversion problem

Open an issue with your Unity and Spine versions, the `conversion-report.md` written by the converter and what you expected to see. A small export that reproduces the problem helps most, as long as you have the right to publish its art.

## Pull requests

The converter targets Unity 6000.6 and reads the documented Spine JSON and atlas formats. Please keep Spine runtime code out of it, since the package ships under MIT.

1. Reference your checkout from a Unity project with `file:` and add the package to `testables`.
2. Run the EditMode tests, and add a test for any behaviour you change.
3. Commit the `.meta` file of every new file or folder: Unity ignores files without one in a package installed from Git.
4. Add a line to `CHANGELOG.md`.
