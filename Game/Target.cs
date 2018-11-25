using System;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

namespace Game
{
    public class Target
    {
        public Grid Grid { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public bool Reached { get; set; }

        private float gridX = 0;
        private float gridY = 0;

        private Storyboard storyboard1 = null;
        private Storyboard storyboard2 = null;
        private Storyboard storyboard3 = null;

        public Target(float x, float y)
        {
            X = x;
            Y = y;

            this.gridX = x - 12.5F;
            this.gridY = y - 12.5F;

            Game.Log("Target (" + x + ", " + y + ")");
        }

        public Grid Create()
        {
            Grid = new Grid();
            Grid.Margin = new Thickness(gridX, gridY, 0, 0);
            Grid.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.VerticalAlignment = VerticalAlignment.Top;

            Ellipse ellipse1 = new Ellipse();
            ellipse1.Width = 25;
            ellipse1.Height = 25;
            ellipse1.Opacity = 0;
            ellipse1.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 204, 0));
            ellipse1.RenderTransformOrigin = new Point(0.5, 0.5);

            ScaleTransform scaleTransform = new ScaleTransform();
            scaleTransform.ScaleX = 1;
            scaleTransform.ScaleY = 1;

            ellipse1.RenderTransform = scaleTransform;

            storyboard1 = new Storyboard();
            storyboard1.Completed += Storyboard1_Completed;

            DoubleAnimation doubleAnimation1 = new DoubleAnimation();
            doubleAnimation1.From = 0;
            doubleAnimation1.To = 0.5;
            doubleAnimation1.Duration = new TimeSpan(0, 0, 1);

            DoubleAnimation doubleAnimation2 = new DoubleAnimation();
            doubleAnimation2.From = 0;
            doubleAnimation2.To = 1;
            doubleAnimation2.Duration = new TimeSpan(0, 0, 1);

            DoubleAnimation doubleAnimation3 = new DoubleAnimation();
            doubleAnimation3.From = 0;
            doubleAnimation3.To = 1;
            doubleAnimation3.Duration = new TimeSpan(0, 0, 1);

            Storyboard.SetTarget(doubleAnimation1, ellipse1);
            Storyboard.SetTargetProperty(doubleAnimation1, "Opacity");

            Storyboard.SetTarget(doubleAnimation2, ellipse1);
            Storyboard.SetTargetProperty(doubleAnimation2, "(Ellipse.RenderTransform).(ScaleTransform.ScaleX)");

            Storyboard.SetTarget(doubleAnimation3, ellipse1);
            Storyboard.SetTargetProperty(doubleAnimation3, "(Ellipse.RenderTransform).(ScaleTransform.ScaleY)");

            storyboard1.Children.Add(doubleAnimation1);
            storyboard1.Children.Add(doubleAnimation2);
            storyboard1.Children.Add(doubleAnimation3);

            Grid.Children.Add(ellipse1);
 
            storyboard2 = new Storyboard();
            storyboard2.Completed += Storyboard2_Completed;

            DoubleAnimation doubleAnimation4 = new DoubleAnimation();
            doubleAnimation4.To = 1;
            doubleAnimation4.Duration = new TimeSpan(0, 0, 0, 0, 500);

            Storyboard.SetTarget(doubleAnimation4, ellipse1);
            Storyboard.SetTargetProperty(doubleAnimation4, "Opacity");

            storyboard2.Children.Add(doubleAnimation4);
            storyboard1.Begin();

            storyboard3 = new Storyboard();
            storyboard3.Completed += Storyboard3_Completed;

            DoubleAnimation doubleAnimation5 = new DoubleAnimation();
            doubleAnimation5.To = 0.5;
            doubleAnimation5.Duration = new TimeSpan(0, 0, 0, 0, 500);

            Storyboard.SetTarget(doubleAnimation5, ellipse1);
            Storyboard.SetTargetProperty(doubleAnimation5, "Opacity");

            storyboard3.Children.Add(doubleAnimation5);
            storyboard3.Begin();

            return Grid;
        }

        private void Storyboard1_Completed(object sender, object e)
        {
            storyboard2.Begin();
        }

        private void Storyboard2_Completed(object sender, object e)
        {
            storyboard3.Begin();
        }

        private void Storyboard3_Completed(object sender, object e)
        {
            storyboard2.Begin();
        }
    }
}
