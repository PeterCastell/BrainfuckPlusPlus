#version 330 core

in vec2 vUV;
flat in int vViewSize;
out vec4 fragColor;

void main() {

    float dist = max(abs(vUV.x), abs(vUV.y)) - float(vViewSize / 2);

    if (dist > 0. && dist < 2.)
        fragColor = vec4(1.);
    else if (dist > 0)
        fragColor = vec4(0., 0., 0., 1.);
    else
        discard;
}