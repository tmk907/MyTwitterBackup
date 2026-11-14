# MyTwitterBackup

Back up your X (Twitter) bookmarks, likes and single posts: text, metadata, photos and videos.

## Features

* Embedded browser (WebView2), start page https://x.com/home. You log in to x.com yourself.
* Single post: open `https://x.com/{user}/status/{id}` and click **Download**.
* Bookmarks: open `https://x.com/i/bookmarks` (or `https://x.com/i/history`), scroll to detect posts, click **Download**.
* Likes: open `https://x.com/{user}/likes` (or `https://x.com/i/history/likes`), scroll, click **Download**.
* Persistent download queue (SQLite). It resumes after a restart and supports retry, pause and clear.
* Downloaded posts browser with Bookmarks / Liked / Other tabs and a preview of the text, media and metadata.
* Media is saved to `{downloads folder}\{user id}\{post id}\`. Default folder: `%USERPROFILE%\Downloads\MyTwitterBackup`.

## Install

Download the `.msix` for your architecture from [Releases](../../releases). If the release includes
`MyTwitterBackup.cer` (self-signed build), import it first to **Local Machine > Trusted People**.

## Build

Requires the .NET 10 SDK and Windows 10 1809 or newer.

```powershell
dotnet build MyTwitterBackup.Desktop\MyTwitterBackup.Desktop.csproj -p:Platform=x64
dotnet test MyTwitterBackup.Tests
winapp run MyTwitterBackup.Desktop   # build, register and launch the packaged app (WinApp CLI)
```

Releases are built by `.github/workflows/release.yml` when a `v*` tag is pushed.
