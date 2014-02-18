using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Sledge.DataStructures.Geometric;
using Sledge.DataStructures.MapObjects;
using Sledge.Common;
using Sledge.Editor.Brushes.Controls;
using Sledge.Extensions;

namespace Sledge.Editor.Brushes
{
    public class ArchBrush : IBrush
    {
        private readonly NumericControl _numSides;
        private readonly NumericControl _wallWidth;
        private readonly NumericControl _arc;
        private readonly NumericControl _startAngle;
        private readonly NumericControl _archHeight;
        private readonly BooleanControl _curvedRamp;
        private readonly NumericControl _tiltPct;

        public ArchBrush()
        {
            _numSides = new NumericControl(this) { LabelText = "Num. sides" };
            _wallWidth = new NumericControl(this) { LabelText = "Wall width", Minimum = 1, Maximum = 1024, Value = 16 };
            _arc = new NumericControl(this) { LabelText = "Arc", Minimum = 1, Maximum = 360 * 4, Value = 360 };
            _startAngle = new NumericControl(this) { LabelText = "Start angle", Minimum = 0, Maximum = 359, Value = 0 };
            _archHeight = new NumericControl(this) { LabelText = "Arch height", Minimum = -1024, Maximum = 1024, Value = 0 };
            _curvedRamp = new BooleanControl(this) { LabelText = "Curved ramp", Checked = false };
            _tiltPct = new NumericControl(this) { LabelText = "Tilt percentage", Minimum = -200, Maximum = 200, Value = 0, Enabled = false };

            _curvedRamp.ValuesChanged += (s, b) => _tiltPct.Enabled = _curvedRamp.GetValue();
        }

        public string Name
        {
            get { return "Arch"; }
        }

        public IEnumerable<BrushControl> GetControls()
        {
            yield return _numSides;
            yield return _wallWidth;
            yield return _arc;
            yield return _startAngle;
            yield return _archHeight;
            yield return _curvedRamp;
            yield return _tiltPct;
        }

        private Solid MakeSolid(IDGenerator generator, IEnumerable<Coordinate[]> faces, ITexture texture, Color col)
        {
            var solid = new Solid(generator.GetNextObjectID()) { Colour = col };
            foreach (var arr in faces)
            {
                var face = new Face(generator.GetNextFaceID())
                {
                    Parent = solid,
                    Plane = new Plane(arr[0], arr[1], arr[2]),
                    Colour = solid.Colour,
                    Texture = { Texture = texture }
                };
                face.Vertices.AddRange(arr.Select(x => new Vertex(x, face)));
                face.UpdateBoundingBox();
                face.AlignTextureToWorld();
                solid.Faces.Add(face);
            }
            solid.UpdateBoundingBox();
            return solid;
        }

        public IEnumerable<MapObject> Create(IDGenerator generator, Box box, ITexture texture)
        {
            var numSides = (int)_numSides.GetValue();
            if (numSides < 3) yield break;
            var wallWidth = _wallWidth.GetValue();
            if (wallWidth < 1) yield break;
            var arc = _arc.GetValue();
            if (arc < 1) yield break;
            var startAngle = _startAngle.GetValue();
            if (startAngle < 0 || startAngle > 359) yield break;
            var archHeight = _archHeight.GetValue();
            var curvedRamp = _curvedRamp.GetValue();
            var tiltPct = curvedRamp ? _tiltPct.GetValue() : 0;
            if (tiltPct < -200 || tiltPct > 200) yield break;
            
            // Very similar to the pipe brush, except with options for start angle, tilt, arc, and height
            var width = box.Width;
            var length = box.Length;
            var height = box.Height;

            var majorOut = width / 2;
            var majorIn = majorOut - wallWidth;
            var minorOut = length / 2;
            var minorIn = minorOut - wallWidth;

            var start = DMath.DegreesToRadians(startAngle);
            var tilt = DMath.Atan(tiltPct / 100);
            var angle = DMath.DegreesToRadians(arc) / numSides;
            var heightAdd = archHeight / numSides;

            var colour = Colour.GetRandomBrushColour();

            // Calculate the coordinates of the inner and outer ellipses' points
            // TODO: Handle height adding for both curving and non-curving arches in a separate method
            var outer = new Coordinate[numSides + 1];
            var inner = new Coordinate[numSides + 1];
            for (var i = 0; i < numSides + 1; i++)
            {
                var a = start + i * angle;
                var h = i * heightAdd;
                var tiltHeight = wallWidth / 2 * DMath.Tan(tilt); // TODO: Interpolation
                
                var xval = box.Center.X + majorOut * DMath.Cos(a);
                var yval = box.Center.Y + minorOut * DMath.Sin(a);
                var zval = box.Start.Z + (curvedRamp ? h + tiltHeight : 0);
                outer[i] = new Coordinate(xval, yval, zval).Round(0);

                xval = box.Center.X + majorIn * DMath.Cos(a);
                yval = box.Center.Y + minorIn * DMath.Sin(a);
                zval = box.Start.Z + (curvedRamp ? h - tiltHeight : 0);
                inner[i] = new Coordinate(xval, yval, zval).Round(0);
            }

            // Create the solids
            for (var i = 0; i < numSides; i++)
            {
                var faces = new List<Coordinate[]>();
                var z = new Coordinate(0, 0, height);

                // Since we are triangulating/splitting each arch segment, we need to generate 2 brushes per side
                if (curvedRamp)
                {
                    // The splitting orientation depends on the curving direction of the arch
                    if (heightAdd >= 0)
                    {
                        faces.Add(new[] { outer[i],       outer[i] + z,   outer[i+1] + z, outer[i+1] });
                        faces.Add(new[] { outer[i+1],     outer[i+1] + z, inner[i] + z,   inner[i]   });
                        faces.Add(new[] { inner[i],       inner[i] + z,   outer[i] + z,   outer[i]   });
                        faces.Add(new[] { outer[i] + z,   inner[i] + z,   outer[i+1] + z  });
                        faces.Add(new[] { outer[i+1],     inner[i],       outer[i]        });
                    }
                    else
                    {
                        faces.Add(new[] { inner[i+1],     inner[i+1] + z, inner[i] + z,   inner[i]   });
                        faces.Add(new[] { outer[i],       outer[i] + z,   inner[i+1] + z, inner[i+1] });
                        faces.Add(new[] { inner[i],       inner[i] + z,   outer[i] + z,   outer[i]   });
                        faces.Add(new[] { inner[i+1] + z, outer[i] + z,   inner[i] + z    });
                        faces.Add(new[] { inner[i],       outer[i],       inner[i+1]      });
                    }
                    yield return MakeSolid(generator, faces, texture, colour);

                    faces.Clear();

                    if (heightAdd >= 0)
                    {
                        faces.Add(new[] { inner[i+1],     inner[i+1] + z, inner[i] + z,   inner[i]   });
                        faces.Add(new[] { inner[i],       inner[i] + z,   outer[i+1] + z, outer[i+1] });
                        faces.Add(new[] { outer[i+1],     outer[i+1] + z, inner[i+1] + z, inner[i+1] });
                        faces.Add(new[] { inner[i+1] + z, outer[i+1] + z, inner[i] + z    });
                        faces.Add(new[] { inner[i],       outer[i+1],     inner[i+1]      });
                    }
                    else
                    {
                        faces.Add(new[] { outer[i],       outer[i] + z,   outer[i+1] + z, outer[i+1] });
                        faces.Add(new[] { inner[i+1],     inner[i+1] + z, outer[i] + z,   outer[i]   });
                        faces.Add(new[] { outer[i+1],     outer[i+1] + z, inner[i+1] + z, inner[i+1] });
                        faces.Add(new[] { outer[i] + z,   inner[i+1] + z, outer[i+1] + z  });
                        faces.Add(new[] { outer[i+1],     inner[i+1],     outer[i]        });
                    }
                    yield return MakeSolid(generator, faces, texture, colour);
                }
                else
                {
                    var h = i * heightAdd * Coordinate.UnitZ;
                    faces.Add(new[] { outer[i],       outer[i] + z,   outer[i+1] + z, outer[i+1]   }.Select(x => x + h).ToArray());
                    faces.Add(new[] { inner[i+1],     inner[i+1] + z, inner[i] + z,   inner[i]     }.Select(x => x + h).ToArray());
                    faces.Add(new[] { outer[i+1],     outer[i+1] + z, inner[i+1] + z, inner[i+1]   }.Select(x => x + h).ToArray());
                    faces.Add(new[] { inner[i],       inner[i] + z,   outer[i] + z,   outer[i]     }.Select(x => x + h).ToArray());
                    faces.Add(new[] { inner[i+1] + z, outer[i+1] + z, outer[i] + z,   inner[i] + z }.Select(x => x + h).ToArray());
                    faces.Add(new[] { inner[i],       outer[i],       outer[i+1],     inner[i+1]   }.Select(x => x + h).ToArray());
                    yield return MakeSolid(generator, faces, texture, colour);
                }
            }
        }
    }
}
