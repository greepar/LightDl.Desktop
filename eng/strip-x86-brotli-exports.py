#!/usr/bin/env python3
"""Copy the x86 runtime's static Brotli archives without DLL-export directives.

StaticLink.Avalonia uses LLD's MinGW mode. In that mode the MSVC /EXPORT:_Foo@N
inside .NET's x86 Brotli archives acquires a second leading underscore. Static
linking needs the functions, but does not need to export them from the desktop EXE.
Only .drectve bytes are replaced with spaces: archive offsets, COFF symbols,
calling conventions and executable code are preserved. The NuGet cache is read-only.
"""
import pathlib
import re
import struct
import sys

EXPORT = re.compile(rb'[/\-]EXPORT:_+[A-Za-z0-9_]*Brotli[A-Za-z0-9_]*(?:@\d+)?(?:,DATA)?')


def strip_exports(data):
    result = bytearray(data)
    if data[:8] != b'!<arch>\n':
        raise ValueError('Not a COFF archive')
    position = 8
    count = 0
    while position + 60 <= len(data):
        header = data[position:position + 60]
        if header[58:60] != b'`\n':
            raise ValueError('Invalid archive member')
        size = int(header[48:58])
        start = position + 60
        member = data[start:start + size]
        name = header[:16].rstrip()
        if name not in (b'/', b'//') and len(member) >= 20:
            machine, sections = struct.unpack_from('<HH', member)
            if machine == 0x14c:  # IMAGE_FILE_MACHINE_I386
                optional_size = struct.unpack_from('<H', member, 16)[0]
                table = 20 + optional_size
                for index in range(sections):
                    offset = table + index * 40
                    section = member[offset:offset + 40]
                    if len(section) != 40:
                        raise ValueError('Truncated COFF section')
                    if section[:8] != b'.drectve':
                        continue
                    length, pointer = struct.unpack_from('<II', section, 16)
                    if pointer + length > len(member):
                        raise ValueError('Truncated directive section')
                    for match in EXPORT.finditer(member, pointer, pointer + length):
                        first, last = start + match.start(), start + match.end()
                        result[first:last] = b' ' * (last - first)
                        count += 1
        position = start + size + (size % 2)
    return bytes(result), count


if __name__ == '__main__':
    source, destination = map(pathlib.Path, sys.argv[1:])
    destination.mkdir(parents=True, exist_ok=True)
    for name in ('brotlicommon.lib', 'brotlienc.lib', 'brotlidec.lib'):
        patched, count = strip_exports((source / name).read_bytes())
        if count == 0:
            raise RuntimeError(f'Expected MSVC x86 Brotli exports in {name}; review this workaround for the new runtime')
        target = destination / name
        if not target.is_file() or target.read_bytes() != patched:
            target.write_bytes(patched)
        print(f'{name}: removed {count} DLL export directives from local static-link copy')
