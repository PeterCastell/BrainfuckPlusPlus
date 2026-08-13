const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const exe = b.addExecutable(.{
        .name = "brainfuck",
        .root_module = b.createModule(.{
            .root_source_file = b.path("template.zig"),
            .target = target,
            .optimize = optimize,
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

    const exe_tests = b.addTest(.{
        .root_module = exe.root_module,
    });

    const run_exe_tests = b.addRunArtifact(exe_tests);

    const test_step = b.step("test", "Run tests");
    test_step.dependOn(&run_exe_tests.step);
}
