using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using Sledge.DataStructures.GameData;
using Sledge.DataStructures.Geometric;
using Sledge.DataStructures.MapObjects;
using Sledge.DataStructures.Rendering;
using Sledge.Editor.Documents;
using Sledge.Editor.UI;
using Sledge.Extensions;
using Sledge.Graphics.Helpers;
using Sledge.Graphics.Shaders;
using Sledge.UI;

namespace Sledge.Editor.Rendering
{
    public class RenderManager
    {
        #region Shaders
        public const string VertexShader = @"#version 330

layout(location = 0) in vec3 position;
layout(location = 1) in vec3 normal;
layout(location = 2) in vec2 texture;
layout(location = 3) in vec3 colour;
layout(location = 4) in float selected;

const vec3 light1direction = vec3(-1, -2, 3);
const vec3 light2direction = vec3(1, 2, 3);
const vec4 light1intensity = vec4(0.6, 0.6, 0.6, 1.0);
const vec4 light2intensity = vec4(0.3, 0.3, 0.3, 1.0);
const vec4 ambient = vec4(0.5, 0.5, 0.5, 1.0);

smooth out vec4 worldPosition;
smooth out vec4 worldNormal;
smooth out vec4 vertexLighting;
smooth out vec4 vertexColour;
smooth out vec2 texCoord;
smooth out float vertexSelected;

uniform bool isWireframe;
uniform bool in3d;
uniform bool showGrid;
uniform float gridSpacing;

uniform mat4 modelViewMatrix;
uniform mat4 perspectiveMatrix;
uniform mat4 cameraMatrix;
uniform mat4 selectionTransform;

void main()
{
    vec4 pos = vec4(position, 1);
    if (selected > 0.9 && (!isWireframe || !in3d)) pos = selectionTransform * pos;
    vec4 modelPos = modelViewMatrix * pos;
    
	vec4 cameraPos = cameraMatrix * modelPos;
	gl_Position = perspectiveMatrix * cameraPos;

    vec4 npos = vec4(normal, 1);
    // http://www.arcsynthesis.org/gltut/Illumination/Tut09%20Normal%20Transformation.html
    if (selected > 0.9 && (!isWireframe || !in3d)) npos = transpose(inverse(selectionTransform)) * npos;
    vec3 normalPos = normalize(npos.xyz);
    npos = vec4(normalPos, 1);

    worldPosition = pos;
    worldNormal = npos;

    float incidence1 = dot(normalPos, light1direction);
    float incidence2 = dot(normalPos, light2direction);

    incidence1 = clamp(incidence1, 0, 1);
    incidence2 = clamp(incidence2, 0, 1);

	vertexColour = vec4(colour, 1);
    vertexLighting = (vec4(1,1,1,1) * light1intensity * incidence1) * 0.5
                   + (vec4(1,1,1,1) * light2intensity * incidence2) * 0.5
                   + (vec4(1,1,1,1) * ambient);
    vertexLighting.w = 1; // Reset the alpha channel or transparency gets messed up later
    texCoord = texture;
    vertexSelected = selected;
}
";

        public const string FragmentShader = @"#version 330

smooth in vec4 worldPosition;
smooth in vec4 worldNormal;
smooth in vec4 vertexColour;
smooth in vec4 vertexLighting;
smooth in vec2 texCoord;
smooth in float vertexSelected;

uniform bool isWireframe;
uniform bool isTextured;
uniform vec4 wireframeColour;
uniform bool in3d;
uniform bool showGrid;
uniform float gridSpacing;
uniform sampler2D currentTexture;

out vec4 outputColor;
void main()
{
    if (in3d && vertexSelected <= 0.9) discard;
    if (isWireframe) {
        if (!in3d && vertexSelected > 0.9) outputColor = vec4(1, 0, 0, 1);
        else if (wireframeColour.w == 0) outputColor = vertexColour;
        else outputColor = wireframeColour;
    } else {
        if (isTextured) {
            outputColor = texture2D(currentTexture, texCoord) * vertexLighting;
        } else {
            outputColor = vertexColour * vertexLighting;
        }
        if (vertexSelected > 0.9) {
            outputColor = outputColor * vec4(1, 0.5, 0.5, 1);
        }
    }
    if (isTextured && showGrid) {
        if (abs(worldNormal).x < 0.9999) outputColor = mix(outputColor, vec4(1, 0, 0, 1), step(mod(worldPosition.x, gridSpacing), 0.5));
        if (abs(worldNormal).y < 0.9999) outputColor = mix(outputColor, vec4(0, 1, 0, 1), step(mod(worldPosition.y, gridSpacing), 0.5));
        if (abs(worldNormal).z < 0.9999) outputColor = mix(outputColor, vec4(0, 0, 1, 1), step(mod(worldPosition.z, gridSpacing), 0.5));
    }
}
";
        #endregion Shaders

        private readonly Document _document;
        private readonly ArrayManager _array;
        public ShaderProgram Shader { get; private set; }

        public Matrix4 Perspective { set { Shader.Set("perspectiveMatrix", value); } }
        public Matrix4 Camera { set { Shader.Set("cameraMatrix", value); } }
        public Matrix4 ModelView { set { Shader.Set("modelViewMatrix", value); } }
        public Matrix4 SelectionTransform { set { Shader.Set("selectionTransform", value); } }
        public bool IsTextured { set { Shader.Set("isTextured", value); } }
        public bool IsWireframe { set { Shader.Set("isWireframe", value); } }
        public bool In3D { set { Shader.Set("in3d", value); } }
        public bool Show3DGrid { set { Shader.Set("showGrid", value); } }
        public float GridSpacing { set { Shader.Set("gridSpacing", value); } }
        public Vector4 WireframeColour { set { Shader.Set("wireframeColour", value); } }

        public Dictionary<ViewportBase, GridRenderable> GridRenderables { get; private set; }  

        public RenderManager(Document document)
        {
            _document = document;
            _array = new ArrayManager(document.Map);
            Shader = new ShaderProgram(
                new Shader(ShaderType.VertexShader, VertexShader),
                new Shader(ShaderType.FragmentShader, FragmentShader));
            GridRenderables = ViewportManager.Viewports.OfType<Viewport2D>().ToDictionary(x => (ViewportBase) x, x => new GridRenderable(_document, x));

            // Set up default values
            Bind();
            Perspective = Camera = ModelView = SelectionTransform = Matrix4.Identity;
            IsTextured = IsWireframe = In3D = false;
            Show3DGrid = document.Map.Show3DGrid;
            GridSpacing = (float) document.Map.GridSpacing;
            WireframeColour = Vector4.Zero;
            Unbind();
        }

        public void Bind()
        {
            Shader.Bind();
        }

        public void Unbind()
        {
            Shader.Unbind();
        }

        public void Draw2D(ViewportBase context, Matrix4 viewport, Matrix4 camera, Matrix4 modelView)
        {
            Bind();
            Perspective = viewport;
            Camera = camera;
            IsTextured = false;
            IsWireframe = true;
            WireframeColour = Vector4.Zero;
            In3D = false;

            ModelView = Matrix4.Identity;
            if (GridRenderables.ContainsKey(context)) GridRenderables[context].Render(context);

            ModelView = modelView;
            _array.DrawWireframe(context, Shader);

            Unbind();

            var vp2d = context as Viewport2D;
            if (vp2d != null && _document.Pointfile != null) DrawPointfile(_document.Pointfile, vp2d.Flatten);
        }

        public void Draw3D(ViewportBase context, Matrix4 viewport, Matrix4 camera, Matrix4 modelView)
        {
            Bind();

            Perspective = viewport;
            Camera = camera;
            ModelView = modelView;
            IsTextured = true;
            IsWireframe = false;
            In3D = false;

            GL.ActiveTexture(TextureUnit.Texture0);
            Shader.Set("currentTexture", 0);
            _array.DrawTextured(context, Shader);

            In3D = true;
            IsWireframe = true;
            WireframeColour = new Vector4(1, 1, 0, 1);

            _array.DrawWireframe(context, Shader);

            Unbind();

            DrawBillboards(context as Viewport3D);
            if (_document.Pointfile != null) DrawPointfile(_document.Pointfile, x => x);
        }

        private void DrawBillboards(Viewport3D vp)
        {
            // These billboards aren't perfect but they'll do (they rotate with the lookat vector rather than the location vector)
            var right = vp.Camera.GetRight();
            var up = vp.Camera.GetUp();
            foreach (Entity entity in _document.Map.WorldSpawn.Find(x => !x.IsVisgroupHidden && !x.IsCodeHidden && x is Entity && ((Entity)x).Sprite != null))
            {
                var orig = new Vector3((float)entity.Origin.X, (float)entity.Origin.Y, (float)entity.Origin.Z);
                var normal = Vector3.Subtract(vp.Camera.Location, orig);

                var tex = entity.Sprite;
                TextureHelper.EnableTexturing();
                GL.Color3(Color.White);
                tex.Bind();

                if (entity.GameData != null)
                {
                    var col = entity.GameData.Properties.FirstOrDefault(x => x.VariableType == VariableType.Color255);
                    if (col != null)
                    {
                        var val = entity.EntityData.Properties.FirstOrDefault(x => x.Key == col.Name);
                        if (val != null)
                        {
                            GL.Color3(val.GetColour(Color.White));
                        }
                    }
                }

                var tup = Vector3.Multiply(up, (float) entity.BoundingBox.Height / 2f);
                var tright = Vector3.Multiply(right, (float) entity.BoundingBox.Width / 2f);

                GL.Begin(BeginMode.Quads);

                GL.Normal3(normal); GL.TexCoord2(1, 1); GL.Vertex3(Vector3.Subtract(orig, Vector3.Add(tup, tright)));
                GL.Normal3(normal); GL.TexCoord2(1, 0); GL.Vertex3(Vector3.Add(orig, Vector3.Subtract(tup, tright)));
                GL.Normal3(normal); GL.TexCoord2(0, 0); GL.Vertex3(Vector3.Add(orig, Vector3.Add(tup, tright)));
                GL.Normal3(normal); GL.TexCoord2(0, 1); GL.Vertex3(Vector3.Subtract(orig, Vector3.Subtract(tup, tright)));

                GL.End();
            }
        }

        private static void DrawPointfile(Pointfile pf, Func<Coordinate, Coordinate> transform)
        {
            TextureHelper.DisableTexturing();
            GL.LineWidth(3);
            GL.Begin(BeginMode.Lines);

            var r = 1f;
            var g = 0.5f;
            var b = 0.5f;
            var change = 0.5f / pf.Lines.Count;

            foreach (var line in pf.Lines)
            {
                var start = transform(line.Start);
                var end = transform(line.End);

                GL.Color3(r, g, b);
                GL.Vertex3(start.DX, start.DY, start.DZ);

                r -= change;
                b += change;

                GL.Color3(r, g, b);
                GL.Vertex3(end.DX, end.DY, end.DZ);
            }

            GL.End();
            GL.LineWidth(1);
            TextureHelper.EnableTexturing();
        }

        public void Update()
        {
            _array.Update(_document.Map);
        }

        public void UpdatePartial(IEnumerable<MapObject> objects)
        {
            _array.UpdatePartial(objects);
            _array.UpdateDecals(_document.Map);
        }

        public void UpdatePartial(IEnumerable<Face> faces)
        {
            _array.UpdatePartial(faces);
            _array.UpdateDecals(_document.Map);
        }

        public void Register(IEnumerable<ViewportBase> viewports)
        {
            foreach (var vp in viewports)
            {
                vp.RenderContext.Add(new RenderManagerRenderable(vp, this));
            }
        }
    }
}