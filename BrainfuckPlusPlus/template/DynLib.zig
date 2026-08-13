const std = @import("std");
const builtin = @import("builtin");
const windows = std.os.windows;

const is_windows = builtin.os.tag == .windows;
const max_path_wide = 2048;

const DynLib = @This();

pub const Error = error{
    BadPathName,
    FileNotFound,
    NameTooLong,
};

const FARPROC = *const fn () callconv(.winapi) void;

extern "kernel32" fn LoadLibraryExW(
    lpLibFileName: [*:0]const u16,
    hFile: ?*anyopaque,
    dwFlags: u32,
) callconv(.winapi) ?windows.HMODULE;

extern "kernel32" fn GetProcAddress(
    hModule: windows.HMODULE,
    lpProcName: [*:0]const u8,
) callconv(.winapi) ?FARPROC;

const Handle = if (is_windows) windows.HMODULE else std.DynLib;

handle: Handle,

pub fn open(path: []const u8) Error!DynLib {
    if (!is_windows) return .{
        .handle = std.DynLib.open(path) catch return error.FileNotFound,
    };

    if (path.len >= max_path_wide) return error.NameTooLong;
    var buf: [max_path_wide]u16 = undefined;

    const len = std.unicode.wtf8ToWtf16Le(&buf, path) catch return error.BadPathName;
    buf[len] = 0;
    const path_w = buf[0..len :0];

    const handle = LoadLibraryExW(path_w.ptr, null, 0) orelse return error.FileNotFound;
    return .{ .handle = handle };
}

pub fn lookup(self: *DynLib, comptime T: type, name: [:0]const u8) ?T {
    if (!is_windows) return self.handle.lookup(T, name);
    const proc = GetProcAddress(self.handle, name.ptr) orelse return null;
    return @ptrCast(@alignCast(proc));
}