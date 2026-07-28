export type ScenePalette = "studio" | "thermal" | "mono";

export interface Scene3DState {
  renderer: "webgl" | "css-fallback";
  palette: ScenePalette;
  speed: number;
  rotationX: number;
  rotationY: number;
  frameCount: number;
}

export interface Scene3DController {
  getState(): Scene3DState;
  setPalette(palette: ScenePalette): void;
  setSpeed(speed: number): void;
  dispose(): void;
}

const paletteColors: Record<ScenePalette, [number[], number[]]> = {
  studio: [
    [0.145, 0.388, 0.922, 1],
    [0.078, 0.722, 0.651, 1],
  ],
  thermal: [
    [0.863, 0.149, 0.149, 1],
    [0.961, 0.62, 0.043, 1],
  ],
  mono: [
    [0.067, 0.094, 0.153, 1],
    [0.898, 0.906, 0.922, 1],
  ],
};

const vertexShaderSource = `
  attribute vec3 aPosition;
  attribute vec3 aNormal;
  uniform mat4 uMatrix;
  uniform vec4 uPrimary;
  uniform vec4 uAccent;
  varying vec4 vColor;

  void main() {
    vec3 light = normalize(vec3(0.35, 0.8, 0.55));
    float brightness = 0.3 + max(dot(normalize(aNormal), light), 0.0) * 0.7;
    float blendAmount = (aPosition.y + aPosition.x + 2.0) * 0.25;
    vColor = mix(uPrimary, uAccent, clamp(blendAmount, 0.0, 1.0)) * vec4(vec3(brightness), 1.0);
    vColor.a = 1.0;
    gl_Position = uMatrix * vec4(aPosition, 1.0);
  }
`;

const fragmentShaderSource = `
  precision mediump float;
  varying vec4 vColor;

  void main() {
    gl_FragColor = vColor;
  }
`;

const positions = new Float32Array([
  -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1, 1, -1, -1, -1, -1, -1, -1, 1, -1, 1,
  1, -1, -1, 1, 1, 1, 1, 1, 1, 1, -1, -1, 1, -1, -1, -1, -1, 1, -1, -1, 1, -1,
  1, -1, -1, 1, 1, -1, 1, 1, -1, -1, 1, 1, -1, 1, 1, 1, -1, -1, -1, -1, -1, 1,
  -1, 1, 1, -1, 1, -1,
]);

const normals = new Float32Array([
  0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0,
  1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 1, 0,
  0, 1, 0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0,
]);

const indices = new Uint16Array([
  0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11, 12, 13, 14, 12, 14,
  15, 16, 17, 18, 16, 18, 19, 20, 21, 22, 20, 22, 23,
]);

export function createScene3D(
  canvas: HTMLCanvasElement,
  fallback: HTMLElement,
  initialPalette: ScenePalette,
  initialSpeed: number,
): Scene3DController {
  const gl = canvas.getContext("webgl", {
    alpha: true,
    antialias: true,
    preserveDrawingBuffer: true,
  });

  if (!gl) {
    canvas.hidden = true;
    fallback.hidden = false;
    return createFallbackController(fallback, initialPalette, initialSpeed);
  }

  fallback.hidden = true;
  const program = createProgram(gl, vertexShaderSource, fragmentShaderSource);
  const positionLocation = gl.getAttribLocation(program, "aPosition");
  const normalLocation = gl.getAttribLocation(program, "aNormal");
  const matrixLocation = requiredUniform(gl, program, "uMatrix");
  const primaryLocation = requiredUniform(gl, program, "uPrimary");
  const accentLocation = requiredUniform(gl, program, "uAccent");

  bindAttribute(gl, positions, positionLocation, 3);
  bindAttribute(gl, normals, normalLocation, 3);
  const indexBuffer = gl.createBuffer();
  if (!indexBuffer) throw new Error("Unable to create WebGL index buffer.");
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indexBuffer);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);

  gl.useProgram(program);
  gl.enable(gl.DEPTH_TEST);
  gl.enable(gl.CULL_FACE);
  gl.clearColor(0, 0, 0, 0);

  let palette = initialPalette;
  let speed = clamp(initialSpeed, 0, 100);
  let rotationX = -0.45;
  let rotationY = 0.65;
  let frameCount = 0;
  let lastFrame = performance.now();
  let animationFrame = 0;
  let pointerId: number | undefined;
  let pointerX = 0;
  let pointerY = 0;

  const draw = (now: number) => {
    const deltaSeconds = Math.min((now - lastFrame) / 1000, 0.1);
    lastFrame = now;
    if (pointerId === undefined) {
      rotationY += deltaSeconds * (0.18 + speed * 0.012);
      rotationX += deltaSeconds * (0.035 + speed * 0.0015);
    }

    resizeCanvas(canvas, gl);
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.useProgram(program);

    const aspect = canvas.width / Math.max(canvas.height, 1);
    const projection = perspective(Math.PI / 4, aspect, 0.1, 100);
    const view = translation(0, 0, -5.2);
    const model = multiply(
      rotationYMatrix(rotationY),
      rotationXMatrix(rotationX),
    );
    gl.uniformMatrix4fv(
      matrixLocation,
      false,
      multiply(projection, multiply(view, model)),
    );
    gl.uniform4fv(primaryLocation, paletteColors[palette][0]);
    gl.uniform4fv(accentLocation, paletteColors[palette][1]);
    gl.drawElements(gl.TRIANGLES, indices.length, gl.UNSIGNED_SHORT, 0);
    frameCount += 1;
    animationFrame = requestAnimationFrame(draw);
  };

  const pointerDown = (event: PointerEvent) => {
    pointerId = event.pointerId;
    pointerX = event.clientX;
    pointerY = event.clientY;
    canvas.setPointerCapture(event.pointerId);
  };
  const pointerMove = (event: PointerEvent) => {
    if (event.pointerId !== pointerId) return;
    rotationY += (event.clientX - pointerX) * 0.012;
    rotationX += (event.clientY - pointerY) * 0.012;
    pointerX = event.clientX;
    pointerY = event.clientY;
  };
  const pointerUp = (event: PointerEvent) => {
    if (event.pointerId !== pointerId) return;
    pointerId = undefined;
    canvas.releasePointerCapture(event.pointerId);
  };
  canvas.addEventListener("pointerdown", pointerDown);
  canvas.addEventListener("pointermove", pointerMove);
  canvas.addEventListener("pointerup", pointerUp);
  canvas.addEventListener("pointercancel", pointerUp);
  animationFrame = requestAnimationFrame(draw);

  return {
    getState: () => ({
      renderer: "webgl",
      palette,
      speed,
      rotationX: Number(rotationX.toFixed(3)),
      rotationY: Number(rotationY.toFixed(3)),
      frameCount,
    }),
    setPalette: (value) => {
      palette = value;
    },
    setSpeed: (value) => {
      speed = clamp(value, 0, 100);
    },
    dispose: () => {
      cancelAnimationFrame(animationFrame);
      canvas.removeEventListener("pointerdown", pointerDown);
      canvas.removeEventListener("pointermove", pointerMove);
      canvas.removeEventListener("pointerup", pointerUp);
      canvas.removeEventListener("pointercancel", pointerUp);
      gl.deleteProgram(program);
      gl.deleteBuffer(indexBuffer);
    },
  };
}

function createFallbackController(
  fallback: HTMLElement,
  initialPalette: ScenePalette,
  initialSpeed: number,
): Scene3DController {
  let palette = initialPalette;
  let speed = clamp(initialSpeed, 0, 100);
  const apply = () => {
    fallback.dataset.palette = palette;
    fallback.style.setProperty(
      "--scene-duration",
      `${Math.max(1.2, 7 - speed * 0.055)}s`,
    );
  };
  apply();
  return {
    getState: () => ({
      renderer: "css-fallback",
      palette,
      speed,
      rotationX: 0,
      rotationY: 0,
      frameCount: 0,
    }),
    setPalette: (value) => {
      palette = value;
      apply();
    },
    setSpeed: (value) => {
      speed = clamp(value, 0, 100);
      apply();
    },
    dispose: () => undefined,
  };
}

function createProgram(
  gl: WebGLRenderingContext,
  vertexSource: string,
  fragmentSource: string,
): WebGLProgram {
  const program = gl.createProgram();
  if (!program) throw new Error("Unable to create WebGL program.");
  gl.attachShader(program, compileShader(gl, gl.VERTEX_SHADER, vertexSource));
  gl.attachShader(
    program,
    compileShader(gl, gl.FRAGMENT_SHADER, fragmentSource),
  );
  gl.linkProgram(program);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    throw new Error(
      gl.getProgramInfoLog(program) ?? "Unable to link WebGL program.",
    );
  }
  return program;
}

function compileShader(
  gl: WebGLRenderingContext,
  kind: number,
  source: string,
): WebGLShader {
  const shader = gl.createShader(kind);
  if (!shader) throw new Error("Unable to create WebGL shader.");
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    throw new Error(
      gl.getShaderInfoLog(shader) ?? "Unable to compile WebGL shader.",
    );
  }
  return shader;
}

function requiredUniform(
  gl: WebGLRenderingContext,
  program: WebGLProgram,
  name: string,
): WebGLUniformLocation {
  const location = gl.getUniformLocation(program, name);
  if (!location) throw new Error(`WebGL uniform '${name}' was not found.`);
  return location;
}

function bindAttribute(
  gl: WebGLRenderingContext,
  values: Float32Array,
  location: number,
  size: number,
): void {
  const buffer = gl.createBuffer();
  if (!buffer) throw new Error("Unable to create WebGL vertex buffer.");
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, values, gl.STATIC_DRAW);
  gl.enableVertexAttribArray(location);
  gl.vertexAttribPointer(location, size, gl.FLOAT, false, 0, 0);
}

function resizeCanvas(
  canvas: HTMLCanvasElement,
  gl: WebGLRenderingContext,
): void {
  const ratio = Math.min(window.devicePixelRatio || 1, 2);
  const width = Math.max(1, Math.floor(canvas.clientWidth * ratio));
  const height = Math.max(1, Math.floor(canvas.clientHeight * ratio));
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
    gl.viewport(0, 0, width, height);
  }
}

function perspective(
  fieldOfView: number,
  aspect: number,
  near: number,
  far: number,
): Float32Array {
  const f = 1 / Math.tan(fieldOfView / 2);
  const range = 1 / (near - far);
  return new Float32Array([
    f / aspect,
    0,
    0,
    0,
    0,
    f,
    0,
    0,
    0,
    0,
    (near + far) * range,
    -1,
    0,
    0,
    near * far * range * 2,
    0,
  ]);
}

function translation(x: number, y: number, z: number): Float32Array {
  return new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1]);
}

function rotationXMatrix(radians: number): Float32Array {
  const cosine = Math.cos(radians);
  const sine = Math.sin(radians);
  return new Float32Array([
    1,
    0,
    0,
    0,
    0,
    cosine,
    sine,
    0,
    0,
    -sine,
    cosine,
    0,
    0,
    0,
    0,
    1,
  ]);
}

function rotationYMatrix(radians: number): Float32Array {
  const cosine = Math.cos(radians);
  const sine = Math.sin(radians);
  return new Float32Array([
    cosine,
    0,
    -sine,
    0,
    0,
    1,
    0,
    0,
    sine,
    0,
    cosine,
    0,
    0,
    0,
    0,
    1,
  ]);
}

function multiply(left: Float32Array, right: Float32Array): Float32Array {
  const output = new Float32Array(16);
  for (let column = 0; column < 4; column += 1) {
    for (let row = 0; row < 4; row += 1) {
      output[column * 4 + row] =
        left[row] * right[column * 4] +
        left[4 + row] * right[column * 4 + 1] +
        left[8 + row] * right[column * 4 + 2] +
        left[12 + row] * right[column * 4 + 3];
    }
  }
  return output;
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}
