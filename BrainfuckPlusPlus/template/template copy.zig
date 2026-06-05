const builtin = @import("builtin");
const std = @import("std");
const ffi = @import("ffi");

const Instant = std.time.Instant;

const Cell = u8; //cell_type

const TapeAllocSize = 1 << 36;
const TapeLength = TapeAllocSize / @sizeOf(Cell);

var threadedIo = std.Io.Threaded.init_single_threaded;

var tapeMemory: [*]Cell = undefined;

var tapeCursor: usize = 0;

var lastTime: Instant = Instant.now();

fn initMemory() !void {
    var basePtr: [*]align(std.heap.page_size_min) u8 = undefined;

    switch (builtin.os.tag) {
        .windows => {
            const windows = std.os.windows;

            const MEM_COMMIT = 0x00001000;
            const MEM_RESERVE = 0x00002000;
            const PAGE_READWRITE = 0x04;

            var basePtrRaw: ?*anyopaque = null;
            var tapeLengthRaw: usize = TapeAllocSize;
            const status = windows.ntdll.NtAllocateVirtualMemory(
                windows.GetCurrentProcess(),
                @as(**anyopaque, @ptrCast(&basePtrRaw)),
                0,
                &tapeLengthRaw,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE,
            );
            if (status != .SUCCESS) {
                return error.AllocationFailed;
            }
            basePtr = @as([*]align(std.heap.page_size_min) u8, @ptrCast(@alignCast(basePtrRaw)));
        },
        else => basePtr = try std.posix.mmap(
            null,
            TapeAllocSize,
            std.posix.PROT.READ | std.posix.PROT.WRITE,
            .{ .TYPE = .PRIVATE, .ANONYMOUS = true },
            -1,
            0,
        ),
    }
    tapeMemory = @ptrCast(basePtr[0..]);
}

fn waitMs(delay: u64) void {
    const duration = delay * std.time.ns_per_ms - (Instant.now() catch unreachable).since(lastTime);
    threadedIo.io().sleep(.fromNanoseconds(duration), .real);
}

inline fn tape() *Cell {
    return tapeAt(tapeCursor);
}

inline fn tapeAt(index: usize) *Cell {
    return &tapeMemory[index];
}

inline fn move(dist: usize) void {
    tapeCursor = (dist + tapeCursor) % TapeLength;
}

inline fn getKeyInput() void {
    var char: [1]u8 = 0;
    std.fs.File.stdin().read(&char);
    tape().* = char[0];
}

fn take_reference() void {
    const ref = &tapeMemory[tapeCursor];

    @memcpy(tapeMemory[tapeCursor..], @as(*[8]u8, @ptrCast(ref)));
}

fn print() void {
    std.debug.print("{c}", .{@bitCast(tape().*)});
}

fn assert_slowpath(cursorPosition: usize, lineNum: i32, columnNum: i32) void {
    @branchHint(.cold);
    std.debug.print("{d},{d}: assert failed!; {d} != {d}\n", .{ lineNum, columnNum, tapeCursor, cursorPosition });
    @panic("assert failed");
}

inline fn assert(cursorPosition: usize, lineNum: i32, columnNum: i32) bool {
    if (tapeCursor != cursorPosition) {
        if (comptime builtin.mode == .Debug or builtin.mode == .ReleaseSafe) {
            assert_slowpath(cursorPosition, lineNum, columnNum);
        } else {
            unreachable;
        }
    }
    return false;
}

inline fn getDigitCount(num: usize) usize {
    if (num == 0) return 1;
    return std.math.log10(num) + (if (num < 0) 2 else 1);
}

fn printCell(lineNum: i32, columnNum: i32) void {
    if (comptime builtin.mode == .Debug)
        std.debug.print("{d},{d}: value at {d} is {d}\n", .{ lineNum, columnNum, tapeCursor, tape().* });
}

fn printDump(position: usize, length: usize, lineNum: i32, columnNum: i32) void {
    if (comptime builtin.mode != .Debug)
        return;

    const end = position +| length;

    std.debug.print("{d},{d}: memory tape dump from {d} to {d}\n|", .{ lineNum, columnNum, position, end - 1 });

    const entryWidth = @max(getDigitCount(position), getDigitCount(end - 1), getDigitCount(std.math.maxInt(Cell)));

    for (position..end) |i| {
        std.debug.print("{[value]d: <[width]}|", .{
            .value = i,
            .width = entryWidth,
        });
    }
    std.debug.print("\n|", .{});

    for (position..end) |i| {
        std.debug.print("{[value]d: <[width]}|", .{
            .value = tapeMemory[i],
            .width = entryWidth,
        });
    }
    std.debug.print("\n", .{});
}

fn scanTape(comptime T: type) fn (start: usize, buf: []T) usize {
    return struct {
        fn inner(start: usize, buf: []T) usize {
            var len: usize = 0;
            for (start..TapeLength) |i| {
                const cell = tapeAt(i).*;
                if (cell == 0 or len >= buf.len)
                    return len;
                buf[len] = @truncate(cell);
                len += 1;
            }
            return len;
        }
    }.inner;
}

fn getFFIType(size: u32) !*ffi.Type {
    const types = [_]*ffi.Type{
        ffi.types.void,
        ffi.types.uint8,
        ffi.types.sint8,
        ffi.types.uint16,
        ffi.types.sint16,
        ffi.types.uint32,
        ffi.types.sint32,
        ffi.types.uint64,
        ffi.types.sint64,
        ffi.types.float,
        ffi.types.double,
        ffi.types.pointer,
        ffi.types.long_double,
        ffi.types.complex_float,
        ffi.types.uchar,
        ffi.types.schar,
        ffi.types.ushort,
        ffi.types.sshort,
        ffi.types.uint,
        ffi.types.sint,
        ffi.types.ulong,
        ffi.types.long,
    };
    if (size > types.len)
        return error.NotAValidType;
    return types[size - 1];
}

const ExternFunc = struct {
    ptr: *const fn () void,
    caller: ffi.Function,
    numParams: usize,
    paramTypes: [MaxScanSize]*ffi.Type,

    const MaxScanSize = 256;
};
var ExternFuncsCount: u8 = 0;
var ExternFuncs: [255]ExternFunc = undefined;

fn createExternPointer() !void {
    if (ExternFuncsCount >= ExternFuncs.len)
        return error.ExternFuncsLimitReached;

    const MaxScanSize = ExternFunc.MaxScanSize;

    var dllName = [_]u8{0} ** (MaxScanSize + 1);
    var funcName = [_]u8{0} ** (MaxScanSize + 1);
    var typeSizes = [_]u8{0} ** MaxScanSize;

    const dllNameLen = scanTape(u8)(tapeCursor + 1, dllName[0..MaxScanSize]);
    const funcNameLen = scanTape(u8)(tapeCursor + 2 + dllNameLen, funcName[0..MaxScanSize]);
    const typeSizesLen = scanTape(u8)(tapeCursor + 3 + dllNameLen + funcNameLen, typeSizes[0..]);

    var lib = try std.DynLib.open(dllName[0..dllNameLen]);

    ExternFuncs[ExternFuncsCount].ptr = lib.lookup(*const fn () void, funcName[0..funcNameLen :0]) orelse return error.FunctionNotFound;

    if (typeSizesLen == 0)
        return error.ReturnTypeMissing;

    const numParams = typeSizesLen - 1;
    var func = &ExternFuncs[ExternFuncsCount];
    func.numParams = numParams;

    for (0..numParams) |i| {
        func.paramTypes[i] = try getFFIType(typeSizes[i + 1]);
    }
    const returnType = try getFFIType(typeSizes[0]);

    try func.caller.prepare(.default(), @intCast(numParams), func.paramTypes[0..], returnType);

    ExternFuncsCount += 1;

    tape().* = ExternFuncsCount;
}

fn executeExternPointer(externIndex: u32) !void {
    if (tapeCursor > TapeLength - 256 * 8 + 1)
        return error.TooCloseToEndOfTape;

    const func = &ExternFuncs[externIndex];
    var args: [256]*anyopaque = undefined;

    for (0..func.numParams) |i| {
        @memcpy(@as(*[8]u8, @ptrCast(&args[i])), tapeMemory[tapeCursor + 1 + i * 8 ..][0..8]);
    }
    var result: [8]u8 align(8) = undefined;

    std.log.debug("{d}", func.paramTypes);

    func.caller.call(func.ptr, @ptrCast(args[0..]), @ptrCast(&result));

    @memcpy(tapeMemory[tapeCursor + 8 * func.numParams + 1 ..][0..result.len], &result);
}

fn externCall() !void {
    const cell = tape().*;

    if (cell == 0) {
        try createExternPointer();
    } else {
        try executeExternPointer(cell - 1);
    }
}

fn run() !void {
    // run_body

    const GLFWwindow = opaque {};
    const GLFWinitFn = *const fn () callconv(.c) i32;
    // const GLFWcreateWindowFn = *const fn (i32, i32, [*:0]const u8, ?*anyopaque, ?*anyopaque) callconv(.c) ?*GLFWwindow;
    const GLFWwindowShouldCloseFn = *const fn (?*GLFWwindow) callconv(.c) i32;
    const GLFWpollEventsFn = *const fn () callconv(.c) void;
    const GLFWterminateFn = *const fn () callconv(.c) void;

    const data = [_]u8{ 0, 'g', 'l', 'f', 'w', '3', '.', 'd', 'l', 'l', 0, 'g', 'l', 'f', 'w', 'C', 'r', 'e', 'a', 't', 'e', 'W', 'i', 'n', 'd', 'o', 'w', 0, 12, 7, 7, 12, 12, 12, 0 };

    @memcpy(tapeMemory[0..], &data);

    try externCall();

    var lib = try std.DynLib.open("glfw3.dll");
    defer lib.close();

    const glfwInit = lib.lookup(GLFWinitFn, "glfwInit") orelse return error.SymbolNotFound;
    const glfwWindowShouldClose = lib.lookup(GLFWwindowShouldCloseFn, "glfwWindowShouldClose") orelse return error.SymbolNotFound;
    const glfwPollEvents = lib.lookup(GLFWpollEventsFn, "glfwPollEvents") orelse return error.SymbolNotFound;
    const glfwTerminate = lib.lookup(GLFWterminateFn, "glfwTerminate") orelse return error.SymbolNotFound;

    if (glfwInit() == 0) return error.GlfwInitFailed;
    defer glfwTerminate();

    var func: ffi.Function = undefined;
    var params = [_]*ffi.Type{ ffi.types.sint32, ffi.types.sint32, ffi.types.pointer, ffi.types.pointer, ffi.types.pointer };

    try func.prepare(.default(), params.len, params[0..], ffi.types.pointer);

    var width: i32 = 800;
    var height: i32 = 600;
    var null_val: ?*anyopaque = null;

    const data2 = [_]*anyopaque{ @ptrCast(&width), @ptrCast(&height), @ptrCast(@constCast(&"Hello World")), @ptrCast(&null_val), @ptrCast(&null_val) };

    @memcpy(tapeMemory[1..], @as(*const [5 * 8]u8, @ptrCast(&data2)));

    try externCall();

    printDump(tapeCursor, 64, 0, 0);

    const window: *GLFWwindow = @as(*align(1) *GLFWwindow, @ptrCast(tapeMemory[5 * 8 + 1 ..][0..8])).*;

    // const window = glfwCreateWindow(800, 600, "Zig Dynamic GLFW", null, null) orelse return error.WindowCreationFailed;
    std.debug.print("Window created successfully!\n", .{});

    // 4. Main Loop
    while (glfwWindowShouldClose(window) == 0) {
        glfwPollEvents();
    }
}

pub fn main() !void {
    try initMemory();
    try run();
}

// var lib = try std.DynLib.open("glfw3.dll");
// const glfwGetError = lib.lookup(*fn (*[*c]const u8) c_int, "glfwGetError") orelse return error.SymbolNotFound;
// var description: [*c]const u8 = undefined;
// const code = glfwGetError(&description);
// std.log.debug("GLFW error {d}: {s}", .{ code, std.mem.span(description) });
