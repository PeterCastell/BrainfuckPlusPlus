const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});
    const mod = b.addModule("brainfuck", .{
        .root_source_file = b.path("template.zig"),
        .target = target,
    });

    const exe = b.addExecutable(.{
        .name = "brainfuck",
        .root_module = b.createModule(.{
            .root_source_file = b.path("template.zig"),
            .target = target,
            .optimize = optimize,
            .imports = &.{
                .{ .name = "brainfuck", .module = mod },
            },
        }),
    });

    const libffi = b.dependency("libffi", .{
        .target = target,
        .optimize = optimize,
    });

    exe.root_module.addImport("ffi", libffi.module("ffi"));

    if (b.systemIntegrationOption("ffi", .{})) {
        exe.root_module.linkSystemLibrary("ffi", .{});
    } else {
        exe.root_module.linkLibrary(libffi.artifact("ffi"));
    }

    b.installArtifact(exe);
}
