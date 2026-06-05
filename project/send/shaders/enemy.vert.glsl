#version 330 core

uniform ivec2 uScreenSize;
uniform ivec2 uPosition;
uniform int uSize;
uniform int uRotation;

void main() {
    vec2 uvs[4] = vec2[](
        vec2(-1.0, -1.0),
        vec2(2.0, 0.0),
        vec2(-1.0, 1.0),
        vec2(0.0, 0.0)
    );

    vec2 uv = uvs[gl_VertexID];

    mat3 viewMat = mat3(
        2./uScreenSize.x, 0,                 0,
        0,                -2./uScreenSize.y, 0,
        0,                0,                 1
    );

    float angle = 3.14159 * float(uRotation) / 255.;

    float c = cos(angle) * float(uSize);
    float s = sin(angle) * float(uSize);
    mat3 xform = mat3(
        c,           s,           0.0,
        -s,          c,           0.0,
        uPosition.x, uPosition.y, 1.0
    );

    gl_Position = vec4((viewMat * xform * vec3(uv, 1.0)).xy, 0.0, 1.0);
}