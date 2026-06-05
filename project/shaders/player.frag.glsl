#version 330 core

in vec2 vUV;

out vec4 fragColor;

const float Threshold = 0.05;

vec2 uvs[4] = vec2[](
    vec2(-1.0, 1.0),
    vec2(0.0, -2.0),
    vec2(1.0, 1.0),
    vec2(0.0, 0.0)
);

float edgeDist(vec2 p, vec2 a, vec2 b) {
    vec2 ab = b - a;
    vec2 ap = p - a;
    float t = dot(ap, ab) / dot(ab, ab);
    
    if (t > -Threshold*.5 && t < 1.+Threshold*.5)
        return length(ap - t * ab);
    else
        return 1;
}



void main() {
    float d0 = edgeDist(vUV, uvs[0], uvs[1]);
    float d1 = edgeDist(vUV, uvs[1], uvs[2]);
    float d2 = edgeDist(vUV, uvs[2], uvs[3]);
    float d3 = edgeDist(vUV, uvs[3], uvs[0]);

    float dist = min(min(d0, d1), min(d2, d3));

    if (dist < Threshold)
        fragColor = vec4(0., 1., 0., 1.);
    else
        discard;
}