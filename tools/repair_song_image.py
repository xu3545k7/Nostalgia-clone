"""Convert one Pillow-supported image to a real PNG for Unity's editor repair hook."""

from pathlib import Path
import sys

from PIL import Image


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: repair_song_image.py INPUT OUTPUT", file=sys.stderr)
        return 2

    source = Path(sys.argv[1])
    destination = Path(sys.argv[2])

    with Image.open(source) as image:
        image.load()
        save_options = {"format": "PNG", "optimize": True}
        icc_profile = image.info.get("icc_profile")
        if icc_profile:
            save_options["icc_profile"] = icc_profile
        image.save(destination, **save_options)

    with Image.open(destination) as check:
        check.verify()
        if check.format != "PNG":
            raise RuntimeError("converter output is not PNG")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
