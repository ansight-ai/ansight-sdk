#!/usr/bin/env python3
"""Verify that Android native libraries support 16 KB memory pages."""

from __future__ import annotations

import argparse
import sys
import zipfile
from pathlib import Path


ELF_MAGIC = b"\x7fELF"
PT_LOAD = 1
REQUIRED_ALIGNMENT = 16 * 1024


class AlignmentError(ValueError):
    """Raised when an ELF file cannot run with the required page size."""


def _read_integer(
    data: bytes,
    offset: int,
    size: int,
    byte_order: str,
    label: str,
) -> int:
    end = offset + size
    if end > len(data):
        raise AlignmentError(f"truncated ELF while reading {label}")
    return int.from_bytes(data[offset:end], byteorder=byte_order)


def verify_elf(data: bytes, label: str, required_alignment: int) -> None:
    if data[:4] != ELF_MAGIC:
        raise AlignmentError(f"{label}: not an ELF file")
    if len(data) < 16:
        raise AlignmentError(f"{label}: truncated ELF header")

    elf_class = data[4]
    encoding = data[5]
    if encoding == 1:
        byte_order = "little"
    elif encoding == 2:
        byte_order = "big"
    else:
        raise AlignmentError(f"{label}: unsupported ELF byte order {encoding}")

    if elf_class == 1:
        program_header_offset = _read_integer(data, 28, 4, byte_order, "e_phoff")
        program_header_size = _read_integer(data, 42, 2, byte_order, "e_phentsize")
        program_header_count = _read_integer(data, 44, 2, byte_order, "e_phnum")
        minimum_header_size = 32
        file_offset_offset = 4
        virtual_address_offset = 8
        alignment_offset = 28
        address_size = 4
    elif elf_class == 2:
        program_header_offset = _read_integer(data, 32, 8, byte_order, "e_phoff")
        program_header_size = _read_integer(data, 54, 2, byte_order, "e_phentsize")
        program_header_count = _read_integer(data, 56, 2, byte_order, "e_phnum")
        minimum_header_size = 56
        file_offset_offset = 8
        virtual_address_offset = 16
        alignment_offset = 48
        address_size = 8
    else:
        raise AlignmentError(f"{label}: unsupported ELF class {elf_class}")

    if program_header_count == 0:
        raise AlignmentError(f"{label}: ELF has no program headers")
    if program_header_size < minimum_header_size:
        raise AlignmentError(
            f"{label}: invalid program-header size {program_header_size}"
        )

    load_segment_count = 0
    for index in range(program_header_count):
        offset = program_header_offset + index * program_header_size
        segment_type = _read_integer(data, offset, 4, byte_order, "p_type")
        if segment_type != PT_LOAD:
            continue

        load_segment_count += 1
        file_offset = _read_integer(
            data,
            offset + file_offset_offset,
            address_size,
            byte_order,
            "p_offset",
        )
        virtual_address = _read_integer(
            data,
            offset + virtual_address_offset,
            address_size,
            byte_order,
            "p_vaddr",
        )
        alignment = _read_integer(
            data,
            offset + alignment_offset,
            address_size,
            byte_order,
            "p_align",
        )

        if alignment < required_alignment:
            raise AlignmentError(
                f"{label}: LOAD segment {index} has {alignment}-byte alignment; "
                f"expected at least {required_alignment}"
            )
        if file_offset % required_alignment != virtual_address % required_alignment:
            raise AlignmentError(
                f"{label}: LOAD segment {index} file and virtual addresses are not "
                f"congruent modulo {required_alignment}"
            )

    if load_segment_count == 0:
        raise AlignmentError(f"{label}: ELF has no LOAD segments")


def verify_path(path: Path, required_alignment: int) -> int:
    if not path.is_file():
        raise AlignmentError(f"{path}: file does not exist")

    if zipfile.is_zipfile(path):
        verified = 0
        with zipfile.ZipFile(path) as archive:
            native_libraries = sorted(
                name
                for name in archive.namelist()
                if name.endswith(".so")
                and (name.startswith("jni/") or name.startswith("lib/"))
            )
            if not native_libraries:
                raise AlignmentError(
                    f"{path}: archive contains no Android native libraries"
                )
            for name in native_libraries:
                verify_elf(archive.read(name), f"{path}!/{name}", required_alignment)
                verified += 1
        return verified

    verify_elf(path.read_bytes(), str(path), required_alignment)
    return 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "paths",
        nargs="+",
        type=Path,
        help="ELF, AAR, APK, or ZIP to verify",
    )
    parser.add_argument(
        "--alignment",
        type=int,
        default=REQUIRED_ALIGNMENT,
        help=f"required page alignment in bytes (default: {REQUIRED_ALIGNMENT})",
    )
    args = parser.parse_args()

    if args.alignment <= 0 or args.alignment & (args.alignment - 1):
        parser.error("--alignment must be a positive power of two")

    try:
        verified = sum(verify_path(path, args.alignment) for path in args.paths)
    except (AlignmentError, OSError, zipfile.BadZipFile) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1

    print(
        f"Verified {verified} Android native librar{'y' if verified == 1 else 'ies'} "
        f"with at least {args.alignment // 1024} KB ELF alignment."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
