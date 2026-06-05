#version 330 core

uniform ivec2 uScreenSize;

out vec2 vUV;
flat out int vViewSize;

void main() {
    vec2 uvs[4] = vec2[](
        vec2(-1.0, -1.0),
        vec2( 1.0, -1.0),
        vec2(-1.0,  1.0),
        vec2( 1.0,  1.0)
    );

    vec2 uv = uvs[gl_VertexID];

    gl_Position = vec4(uv.x, uv.y, 0., 1.);

    vViewSize = min(uScreenSize.x, uScreenSize.y);

    vUV = uv * uScreenSize / 2.;
}