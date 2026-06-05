const builtin = @import("builtin");
const std = @import("std");
const ffi = @import("ffi");

const Instant = std.time.Instant;

const Cell = u8; //cell_type

const anyfunc = fn () callconv(.c) void;

const TapeAllocSize = 1 << 36;
const TapeLength = TapeAllocSize / @sizeOf(Cell);

var tapeMemory: [*]Cell = undefined;

var tapeCursor: usize = 0;

var lastTime: Instant = undefined;

var libMap: std.StringHashMap(std.DynLib) = undefined;

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
    const now = Instant.now() catch unreachable;
    const delayNs = delay * std.time.ns_per_ms;
    const elapsed = now.since(lastTime);
    if (delayNs > elapsed)
        std.Thread.sleep(delayNs - elapsed);
    lastTime = now;
}

fn tape() *Cell {
    return tapeAt(tapeCursor);
}

fn tapeAt(index: usize) *Cell {
    return &tapeMemory[index];
}

fn moveLeft(dist: usize) void {
    moveRight(TapeLength - (dist % TapeLength));
}

fn moveRight(dist: usize) void {
    tapeCursor = (tapeCursor + dist) % TapeLength;
}

fn whileCondition() bool {
    return tape().* != 0;
}

fn increase(amount: Cell) void {
    tape().* +%= amount;
}
fn decrease(amount: Cell) void {
    tape().* -%= amount;
}

fn increaseString(string: []const u8) void {
    writeTape(u8)(tapeCursor, string);
}
fn decreaseString(string: []const u8) void {
    writeTapeSub(u8)(tapeCursor, string);
}

fn takeReference() void {
    const ptr = &tapeMemory[tapeCursor];
    writeTape(u8)(tapeCursor, @as(*const [8]u8, @ptrCast(&ptr)));
}

fn dereference() void {
    var ptr: *const u8 = undefined;
    readTape(u8)(tapeCursor, @as(*[8]u8, @ptrCast(&ptr)));
    writeTape(u8)(tapeCursor + 8, @as(*const [1]u8, @ptrCast(ptr)));
}

fn read() !void {
    var char: [1]u8 = 0;
    _ = try std.fs.File.stdin().read(&char);
    tape().* = char[0];
}

fn print() !void {
    const char = [1]u8{@truncate(tape().*)};
    _ = try std.fs.File.stdout().write(&char);
}

fn assertSlowpath(cursorPosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    @branchHint(.cold);
    if (message) |msg| {
        std.debug.print("{s} ({d}, {d}): assert failed! ({d} != {d}); {s}\n", .{ file, lineNum, columnNum, tapeCursor, cursorPosition, msg });
    } else std.debug.print("{s} ({d}, {d}): assert failed! ({d} != {d})\n", .{ file, lineNum, columnNum, tapeCursor, cursorPosition });
    @panic("assert failed");
}

fn assert(cursorPosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    if (tapeCursor != cursorPosition) {
        if (comptime builtin.mode == .Debug or builtin.mode == .ReleaseSafe) {
            assertSlowpath(cursorPosition, lineNum, columnNum, file, message);
        } else {
            unreachable;
        }
    }
}

fn assertRelative(offset: isize, cursorPosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    const testPosition = (cursorPosition + @as(usize, @intCast(@mod(offset, TapeLength)))) % TapeLength;
    if (tapeCursor != testPosition) {
        if (comptime builtin.mode == .Debug or builtin.mode == .ReleaseSafe) {
            assertSlowpath(testPosition, lineNum, columnNum, file, message);
        } else {
            unreachable;
        }
    }
}

fn printDebugMessage(lineNum: i32, columnNum: i32, file: []const u8, message: []const u8) void {
    if (comptime builtin.mode != .Debug)
        return;
    std.debug.print("{s} ({d}, {d}): {s}\n", .{ file, lineNum, columnNum, message });
}

fn getDigitCount(num: usize) usize {
    if (num == 0) return 1;
    return std.math.log10(num) + 1;
}
fn getSignedDigitCount(num: usize) u64 {
    if (num == 0) return 1;
    const pos: isize = @bitCast(num % TapeLength);
    const spos = if (pos > TapeLength / 2) pos - TapeLength else pos;
    return std.math.log10(@abs(spos)) + 2;
}

fn getSignedTapePos(pos: usize) isize {
    const spos: isize = @bitCast(pos % TapeLength);
    return if (spos > TapeLength / 2) spos - TapeLength else spos;
}

fn printCell(lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    if (comptime builtin.mode != .Debug)
        return;

    if (message) |msg| {
        std.debug.print("{s} ({d}, {d}): value at {d} is {d}; {s}\n", .{ file, lineNum, columnNum, getSignedTapePos(tapeCursor), tape().*, msg });
    } else std.debug.print("{s} ({d}, {d}): value at {d} is {d}\n", .{ file, lineNum, columnNum, getSignedTapePos(tapeCursor), tape().* });
}

fn printDump(length: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    if (comptime builtin.mode != .Debug)
        return;

    const end = tapeCursor + length;

    if (message) |msg| {
        std.debug.print("{s} ({d}, {d}): memory tape dump from {d} to {d}; {s}\n|", .{ file, lineNum, columnNum, tapeCursor, end - 1, msg });
    } else std.debug.print("{s} ({d}, {d}): memory tape dump from {d} to {d}\n|", .{ file, lineNum, columnNum, tapeCursor, end - 1 });

    const entryWidth = @max(getSignedDigitCount(tapeCursor), getSignedDigitCount(end - 1), getDigitCount(std.math.maxInt(Cell)));

    for (tapeCursor..end) |i| {
        std.debug.print("{[value]d:<[width]}|", .{
            .value = getSignedTapePos(i),
            .width = entryWidth,
        });
    }
    std.debug.print("\n|", .{});

    for (tapeCursor..end) |i| {
        const pos = i % TapeLength;
        std.debug.print("{[value]d:<[width]}|", .{
            .value = tapeMemory[pos],
            .width = entryWidth,
        });
    }
    std.debug.print("\n", .{});
}

fn debugQuit(lineNum: i32, columnNum: i32, file: []const u8) void {
    if (comptime builtin.mode != .Debug)
        return;

    std.debug.print("{s} ({d}, {d}): quitting program\n", .{ file, lineNum, columnNum });
    @panic("debug quit");
}

// reads into buffer until end or zero byte is reached
fn scanTape(comptime T: type) fn (start: usize, buf: []T) usize {
    return struct {
        fn inner(start: usize, buf: []T) usize {
            var len: usize = 0;
            for (0..buf.len) |i| {
                const cell = tapeAt((start + i) % TapeLength).*;
                if (cell == 0)
                    return len;
                buf[len] = @truncate(cell);
                len += 1;
            }
            return len;
        }
    }.inner;
}

// reads into buffer until end is reached
fn readTape(comptime T: type) fn (start: usize, buf: []T) void {
    return struct {
        fn inner(start: usize, buf: []T) void {
            for (0..buf.len) |i| {
                const cell = tapeAt((start + i) % TapeLength).*;
                buf[i] = @truncate(cell);
            }
        }
    }.inner;
}
// adds the buffer onto the tape
fn writeTape(comptime T: type) fn (start: usize, buf: []const T) void {
    return struct {
        fn inner(start: usize, buf: []const T) void {
            for (0..buf.len) |i| {
                tapeAt((start + i) % TapeLength).* +%= @truncate(buf[i]);
            }
        }
    }.inner;
}
// subtracts the buffer onto the tape
fn writeTapeSub(comptime T: type) fn (start: usize, buf: []const T) void {
    return struct {
        fn inner(start: usize, buf: []const T) void {
            for (0..buf.len) |i| {
                tapeAt((start + i) % TapeLength).* -%= @truncate(buf[i]);
            }
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

const ExternCaller = struct {
    caller: ffi.Function,
    numParams: usize,
    paramTypes: [MaxParamCount]*ffi.Type,

    const MaxParamCount = 256;
};
var ExternFuncsCount: Cell = 0;
var ExternFuncs: [std.math.maxInt(Cell)]ExternCaller = undefined;

fn createExternCaller(line: u32, col: u32, file: []const u8) !void {
    if (ExternFuncsCount >= ExternFuncs.len)
        return error.ExternFuncsLimitReached;

    const MaxScanSize = ExternCaller.MaxParamCount;

    var typeSizes = [_]u8{0} ** MaxScanSize;
    const typeSizesLen = scanTape(u8)(tapeCursor, typeSizes[0..]);

    var func = &ExternFuncs[ExternFuncsCount];

    if (typeSizesLen == 0)
        return error.ReturnTypeMissing;

    const numParams = typeSizesLen - 1;
    func.numParams = numParams;

    for (0..numParams) |i| {
        func.paramTypes[i] = getFFIType(typeSizes[i + 1]) catch |err| {
            std.debug.print("{s} ({d}, {d}): type index out of bounds for argument {d}\n", .{ file, line, col, i + 1 });
            return err;
        };
    }
    const returnType = getFFIType(typeSizes[0]) catch |err| {
        std.debug.print("{s} ({d}, {d}): type index out of bounds for return type\n", .{ file, line, col });
        return err;
    };

    func.caller.prepare(.default(), @intCast(numParams), func.paramTypes[0..], returnType) catch |err| {
        std.debug.print("{s} ({d}, {d}): failed to create exten function caller\n", .{ file, line, col });
        return err;
    };

    tapeAt(tapeCursor + numParams).* +%= ExternFuncsCount;

    ExternFuncsCount += 1;
}

fn findExternFunction(line: u32, col: u32, file: []const u8) !void {
    const MaxScanSize = 256;

    var dllName = [_]u8{0} ** (MaxScanSize + 1);
    var funcName = [_]u8{0} ** (MaxScanSize + 1);

    const dllNameLen = scanTape(u8)(tapeCursor, dllName[0..MaxScanSize]);
    const funcNameLen = scanTape(u8)(tapeCursor + 1 + dllNameLen, funcName[0..MaxScanSize]);

    const dllNameSpan = dllName[0..dllNameLen];

    if (!libMap.contains(dllNameSpan))
        try libMap.put(dllNameSpan, std.DynLib.open(dllNameSpan) catch |err| {
            std.debug.print("{s} ({d}, {d}): couldn't open library \"{s}\"\n", .{ file, line, col, dllNameSpan });
            return err;
        });

    var lib = libMap.getPtr(dllNameSpan) orelse return error.LibNotFound;

    const func = lib.lookup(*const fn () callconv(.c) void, funcName[0..funcNameLen :0]) orelse {
        std.debug.print("{s} ({d}, {d}): couldn't find function \"{s}\"\n", .{ file, line, col, funcName[0..funcNameLen] });
        return error.FunctionNotFound;
    };

    writeTape(u8)(tapeCursor + 2 + dllNameLen + funcNameLen, @as(*const [8]u8, @ptrCast(&func)));
}

fn callExternFunction() !void {
    if (tapeCursor > TapeLength - 256 * 8 + 1)
        return error.TooCloseToEndOfTape;

    const externIndex = tape().*;
    const func = &ExternFuncs[externIndex];

    var funcPtr: *anyfunc = undefined;
    var result: *anyopaque = undefined;
    var args: [256]*anyopaque = undefined;

    readTape(u8)(tapeCursor + 1, @as(*[8]u8, @ptrCast(&funcPtr)));

    readTape(u8)(tapeCursor + 9, @as(*[8]u8, @ptrCast(&result)));

    readTape(u8)(tapeCursor + 17, @as(*[256 * 8]u8, @ptrCast(&args)));

    func.caller.call(funcPtr, @ptrCast(args[0..]), result);
}

fn run() !void {
    // #
}

pub fn main() !void {
    var gpa = std.heap.GeneralPurposeAllocator(.{}){};
    defer _ = gpa.deinit();

    libMap = std.StringHashMap(std.DynLib).init(gpa.allocator());
    defer libMap.deinit();

    lastTime = Instant.now() catch unreachable;

    try initMemory();
    try run();
}
