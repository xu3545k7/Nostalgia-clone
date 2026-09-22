Nostalgia-clone

This folder contains the Unity project `nos_clone`.

To clone and build on another machine:

- Clone the repo:
  git clone https://github.com/xu3545k7/Nostalgia-clone.git
- Enter the UnityProject/nos_clone folder in Unity Hub or open the project in Unity Editor.
- If the repo uses Git LFS, run:
  git lfs install
  git lfs pull

Included: `Assets/`, `ProjectSettings/`, `Packages/` and `.meta` files.
Excluded: `Library/`, `Temp/`, `Build/` and other generated caches.

## Player song library

The song-selection screen includes a **曲目管理** button. Player builds can:

- choose a song folder's `register.json` (or paste its full path);
- validate required chart/audio files before importing;
- assign a category and immediately refresh the song carousel;
- edit the selected imported song's title, author, or category;
- open the writable library folder; and
- delete an imported song after a two-click confirmation.

Imports are copied to `Application.persistentDataPath/UserSongs`; packaged songs
under `Assets/Resources/songs` remain read-only. A song folder uses the existing
`register.json` format and may contain JSON charts, WAV/OGG/MP3/AIFF audio, and
PNG/JPG cover images. Paths may be relative to the imported folder or use the
existing `songs/<folder>/...` Resources-style form.
