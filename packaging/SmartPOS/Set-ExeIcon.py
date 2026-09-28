import sys
import struct
import ctypes
from ctypes import wintypes

kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)

BeginUpdateResourceW = kernel32.BeginUpdateResourceW
BeginUpdateResourceW.argtypes = [wintypes.LPCWSTR, wintypes.BOOL]
BeginUpdateResourceW.restype = wintypes.HANDLE

UpdateResourceW = kernel32.UpdateResourceW
UpdateResourceW.argtypes = [wintypes.HANDLE, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.WORD, wintypes.LPVOID, wintypes.DWORD]
UpdateResourceW.restype = wintypes.BOOL

EndUpdateResourceW = kernel32.EndUpdateResourceW
EndUpdateResourceW.argtypes = [wintypes.HANDLE, wintypes.BOOL]
EndUpdateResourceW.restype = wintypes.BOOL

RT_ICON = ctypes.cast(3, wintypes.LPCWSTR)
RT_GROUP_ICON = ctypes.cast(14, wintypes.LPCWSTR)

def inject_icon(exe_path, ico_path):
    with open(ico_path, 'rb') as f:
        ico_data = f.read()

    # Parse ICONDIR
    reserved, ico_type, count = struct.unpack_from('<HHH', ico_data, 0)
    if ico_type != 1:
        raise ValueError("Not a valid ICO file")

    entries = []
    offset = 6
    for i in range(count):
        width, height, colors, res, planes, bpp, size, img_offset = struct.unpack_from('<BBBBHHII', ico_data, offset)
        entries.append({
            'width': width,
            'height': height,
            'colors': colors,
            'reserved': res,
            'planes': planes,
            'bpp': bpp,
            'size': size,
            'offset': img_offset,
            'id': i + 1
        })
        offset += 16

    # Build GRPICONDIR + GRPICONDIRENTRY
    # GRPICONDIR: Reserved(2), Type(2), Count(2)
    # GRPICONDIRENTRY: Width(1), Height(1), Colors(1), Reserved(1), Planes(2), BitCount(2), BytesInRes(4), ID(2)
    grp_header = bytearray()
    grp_header.extend(struct.pack('<HHH', 0, 1, count))
    for e in entries:
        grp_header.extend(struct.pack(
            '<BBBBHHIH',
            e['width'],
            e['height'],
            e['colors'],
            e['reserved'],
            e['planes'],
            e['bpp'],
            e['size'],
            e['id']
        ))

    h_update = BeginUpdateResourceW(exe_path, False)
    if not h_update:
        err = ctypes.get_last_error()
        raise WindowsError(f"BeginUpdateResourceW failed with error {err}")

    # Write each RT_ICON
    for e in entries:
        img_data = ico_data[e['offset']:e['offset'] + e['size']]
        c_img_data = (ctypes.c_char * len(img_data)).from_buffer_copy(img_data)
        res_id = ctypes.cast(e['id'], wintypes.LPCWSTR)
        res = UpdateResourceW(h_update, RT_ICON, res_id, 0, c_img_data, len(img_data))
        if not res:
            err = ctypes.get_last_error()
            EndUpdateResourceW(h_update, True)
            raise WindowsError(f"UpdateResourceW RT_ICON {e['id']} failed with error {err}")

    # Write RT_GROUP_ICON (ID 1 or ID 'MAINICON' or 100)
    # IExpress / standard Win32 executables usually use icon resource 1 or MAINICON
    # We can write to resource ID 1 and '1'
    c_grp_data = (ctypes.c_char * len(grp_header)).from_buffer_copy(grp_header)
    res_id_grp = ctypes.cast(1, wintypes.LPCWSTR)
    res = UpdateResourceW(h_update, RT_GROUP_ICON, res_id_grp, 0, c_grp_data, len(grp_header))
    if not res:
        err = ctypes.get_last_error()
        EndUpdateResourceW(h_update, True)
        raise WindowsError(f"UpdateResourceW RT_GROUP_ICON failed with error {err}")

    # Also update IExpress standard icon name if any, e.g. ID 1
    if not EndUpdateResourceW(h_update, False):
        err = ctypes.get_last_error()
        raise WindowsError(f"EndUpdateResourceW failed with error {err}")

    print(f"Successfully injected {count} icon images into {exe_path}")

if __name__ == '__main__':
    exe_p = sys.argv[1] if len(sys.argv) > 1 else r"D:\POS\artifacts\SmartPOS-Setup.exe"
    ico_p = sys.argv[2] if len(sys.argv) > 2 else r"D:\POS\packaging\SmartPOS\SmartPOS.ico"
    inject_icon(exe_p, ico_p)
