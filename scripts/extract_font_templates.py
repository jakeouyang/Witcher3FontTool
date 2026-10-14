"""Extract registration-only SWFs and code tables from a locally installed game.

No original glyph outlines are retained in the generated templates.
Usage: python scripts/extract_font_templates.py GAME_ROOT RESOURCE_DIRECTORY ru ua
"""
import pathlib
import struct
import sys
import zlib


def extract(game, output, languages):
    output.mkdir(parents=True, exist_ok=True)
    with (game / "content/content0/bundles/r4gui.bundle").open("rb") as bundle:
        header = bundle.read(32)
        if header[:8] != b"POTATO70":
            raise ValueError("Unexpected bundle format")
        index = bundle.read(struct.unpack_from("<I", header, 16)[0])
        for language in languages:
            path = f"gameplay\\gui_new\\swf\\witcher3\\fonts_{language}.redswf".encode()
            entry = next(index[i:i + 304] for i in range(0, len(index), 304)
                         if index[i:i + 256].split(b"\0")[0] == path)
            offset, _, size, packed = struct.unpack_from("<4I", entry, 272)
            if struct.unpack_from("<I", entry, 292)[0] != 1:
                raise ValueError("Expected a zlib resource")
            bundle.seek(offset)
            raw = zlib.decompress(bundle.read(packed))
            if len(raw) != size:
                raise ValueError("Invalid resource size")
            start = raw.index(b"CFX")
            swf = b"FWS" + raw[start + 3:start + 8] + zlib.decompress(raw[start + 8:])
            position = 8 + (5 + (swf[8] >> 3) * 4 + 7) // 8 + 4
            result = bytearray(swf[:position])
            codes = set()
            while position + 2 <= len(swf):
                word, = struct.unpack_from("<H", swf, position)
                position += 2
                code, length = word >> 6, word & 63
                if length == 63:
                    length, = struct.unpack_from("<I", swf, position)
                    position += 4
                data = swf[position:position + length]
                position += length
                if code == 75:
                    table = 7 + data[4]
                    count, = struct.unpack_from("<H", data, table - 2)
                    width = 4 if data[2] & 8 else 2
                    code_offset, = struct.unpack_from("<I" if width == 4 else "<H", data, table + count * width)
                    wide = bool(data[2] & 4)
                    codes.update(struct.unpack_from("<H" if wide else "<B", data, table + code_offset + i * (2 if wide else 1))[0]
                                 for i in range(count))
                    print(language, "slot", struct.unpack_from("<H", data)[0], "glyphs", count,
                          "layout", struct.unpack_from("<3h", data, table + code_offset + count * (2 if wide else 1)))
                    data = data[:2] + bytes([data[2] & 127]) + data[3:table - 2] + b"\0\0"
                result += struct.pack("<HI", code << 6 | 63, len(data)) + data
                if code == 0:
                    break
            struct.pack_into("<I", result, 4, len(result))
            (output / f"template-{language}.swf").write_bytes(result)
            (output / f"charset-game-{language}.txt").write_text("".join(chr(c) for c in sorted(codes)), encoding="utf-8")


if __name__ == "__main__":
    extract(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3:])
