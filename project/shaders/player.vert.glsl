#version 330 core

uniform ivec2 uScreenSize;
uniform ivec2 uPosition;
uniform int uSize;
uniform int uRotation;

out vec2 vUV;

vec2 uvs[4] = vec2[](
    vec2(-1.0, 1.0),
    vec2(0.0, -2.0),
    vec2(0.0, 0.0),
    vec2(1.0, 1.0)
);

void main() {

    vec2 uv = uvs[gl_VertexID];

    float viewScale = float(min(uScreenSize.x, uScreenSize.y)) / 256.;

    mat3 viewMat = mat3(
        2. * viewScale / uScreenSize.x, 0,                 0,
        0,                -2. * viewScale / uScreenSize.y, 0,
        0,                0,                 1
    );

    float angle = 3.14159 * 2. * float(uRotation) / 255.;

    vec2 wPosition = mod((vec2(uPosition) / 256. + vec2(128)), vec2(256)) - vec2(128);

    float c = cos(angle) * float(uSize);
    float s = sin(angle) * float(uSize);
    mat3 xform = mat3(
        c,           s,           0.0,
        -s,          c,           0.0,
        wPosition.x, wPosition.y, 1.0
    );

    gl_Position = vec4((viewMat * xform * vec3(uv, 1.0)).xy, 0.0, 1.0);

    vUV = uv;
}