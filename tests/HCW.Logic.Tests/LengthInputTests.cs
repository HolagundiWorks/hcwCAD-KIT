using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class LengthInputTests
    {
        [Theory]
        [InlineData(1050, 1.0, 1050)]                 // mm drawing
        [InlineData(1050, 0.1, 1050)]                 // cm
        [InlineData(1050, 0.001, 1050)]               // m
        [InlineData(1050, 1 / 25.4, 1050)]            // inches
        [InlineData(1050, 1 / 304.8, 1050)]           // feet: 3.4449 shown, 1050 must come back
        [InlineData(2100, 1 / 304.8, 2100)]
        public void TheUntouchedDefaultComesBackExactly(double defaultMm, double perMm, double expected)
        {
            double shown = System.Math.Round(defaultMm * perMm, 4);
            Assert.Equal(expected, LengthInput.ToMm(shown, defaultMm, perMm));
        }

        [Theory]
        [InlineData(36, 1 / 25.4, 914.4)]             // 36 in
        [InlineData(3, 1 / 304.8, 914.4)]             // 3 ft
        [InlineData(90, 0.1, 900)]                    // 90 cm
        [InlineData(0.9, 0.001, 900)]                 // 0.9 m
        [InlineData(900, 1.0, 900)]                   // 900 mm
        public void ATypedValueIsConvertedToMillimetres(double typed, double perMm, double expected)
        {
            Assert.Equal(expected, LengthInput.ToMm(typed, 1234, perMm), 2);
        }

        [Fact]
        public void ANonPositiveScaleIsRefused()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => LengthInput.ToMm(1, 1, 0));
        }
    }
}
