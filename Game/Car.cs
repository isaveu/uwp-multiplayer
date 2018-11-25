using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace Game
{
    public class Car
    {
        public Grid Grid { get; set; }
        public bool Moving { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Angle { get; set; }
        public bool Created { get; set; }

        private Storyboard showStoryboard = null;
        private Storyboard hideStoryboard = null;

        private float vx = 0;
        private float vy = 0;

        private float topSpeed = 20F;
        private float acceleration = 1.5F;
        private float brakes = 1F;
        private float friction = 0.5F;
        private float handeling = 5F;
        private float speed = 0;

        private Image image = null;
        private RotateTransform rotateTransfrom = null;

        private TextBlock textBlock = null;

        public Car(float x, float y)
        {
            this.X = x;
            this.Y = y; 

            Game.Log("Car (" + X + ", " + Y + ")");

            this.Angle = 270F;
            this.speed = 0;
        }

        public Grid Create(string playerId)
        {
            Grid = new Grid()
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(X - 110, Y - 110, 0, 0),
                Width = 220,
                Height = 220,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };

            ScaleTransform scaleTransfrom = new ScaleTransform()
            {
                ScaleX = 0,
                ScaleY = 0
            };

            Grid.RenderTransform = scaleTransfrom;

            showStoryboard = new Storyboard();

            DoubleAnimation doubleAnimation1 = new DoubleAnimation()
            {
                From = 0,
                To = 1,
                Duration = new TimeSpan(0, 0, 0, 0, 100)
            };

            DoubleAnimation doubleAnimation2 = new DoubleAnimation()
            {
                From = 0,
                To = 1,
                Duration = new Duration(new TimeSpan(0, 0, 0, 0, 200))
            };

            DoubleAnimation doubleAnimation3 = new DoubleAnimation()
            {
                From = 0,
                To = 1,
                Duration = new Duration(new TimeSpan(0, 0, 0, 0, 200))
            };

            Storyboard.SetTarget(doubleAnimation1, Grid);
            Storyboard.SetTargetProperty(doubleAnimation1, "Opacity");

            Storyboard.SetTarget(doubleAnimation2, Grid);
            Storyboard.SetTargetProperty(doubleAnimation2, "(Grid.RenderTransform).(ScaleTransform.ScaleX)");

            Storyboard.SetTarget(doubleAnimation3, Grid);
            Storyboard.SetTargetProperty(doubleAnimation3, "(Grid.RenderTransform).(ScaleTransform.ScaleY)");

            showStoryboard.Children.Add(doubleAnimation1);
            showStoryboard.Children.Add(doubleAnimation2);
            showStoryboard.Children.Add(doubleAnimation3);

            hideStoryboard = new Storyboard();

            DoubleAnimation doubleAnimation4 = new DoubleAnimation()
            {
                To = 0,
                Duration = new TimeSpan(0, 0, 0, 0, 100)
            };

            Storyboard.SetTarget(doubleAnimation4, Grid);
            Storyboard.SetTargetProperty(doubleAnimation4, "Opacity");

            hideStoryboard.Children.Add(doubleAnimation4);

            BitmapImage bitmapImage = null;

            if (playerId == null)
            {
                bitmapImage = new BitmapImage(new Uri("ms-appx://Game/Assets/Car-Local.png"));
            }
            else
            {
                bitmapImage = new BitmapImage(new Uri("ms-appx://Game/Assets/Car-Remote.png"));
            }

            image = new Image()
            {
                Source = bitmapImage,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Width = 50,
                Height = 50
            };

            Grid.Children.Add(image);

            if (playerId != null)
            {
                textBlock = new TextBlock()
                {
                    Text = playerId,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                Grid.Children.Add(textBlock);
            }

            rotateTransfrom = new RotateTransform();

            return Grid;
        }

        public void Update(bool left, bool up, bool right, bool down, float mapWidth, float mapHeight)
        {
            handeling = 5;

            if (speed > 0 || speed < 0)
            {
                Moving = true;
            }
            else
            {
                Moving = false;
            }

            if (up)
            {
                if (speed < topSpeed)
                {
                    speed = speed + acceleration;
                }
            }
            else if (down)
            {
                speed = speed - brakes;
            }

            if (left)
            {
                Angle -= handeling * speed / topSpeed;

                if (Angle < 0)
                {
                    Angle = 360 + Angle;
                }
            }
            else if (right)
            {
                Angle += handeling * speed / topSpeed;

                if (Angle > 360)
                {
                    Angle = Angle - 360;
                }
            }

            if (speed > 0)
            {
                speed = speed - friction;
            }
            else if (speed < 0)
            {
                speed = speed + friction;
            }

            if (X < 25)
            {
                X = 26;
                speed = -speed;
            }
            else if (X > (mapWidth - 25))
            {
                X = (mapWidth - 26);
                speed = -speed;
            }

            if (Y < 25)
            {
                Y = 26;
                speed = -speed;
            }
            else if (Y > (mapHeight - 25))
            {
                Y = (mapHeight - 26);
                speed = -speed;
            }

            vx = (float)Math.Cos(Angle * Math.PI / 180) * speed;
            vy = (float)Math.Sin(Angle * Math.PI / 180) * speed;

            X = X + vx;
            Y = Y + vy;
        }

        public void Show()
        {
            rotateTransfrom.Angle = Angle;
            image.RenderTransform = rotateTransfrom;
            showStoryboard.Begin();
        }

        public void Draw()
        {
            if (Grid != null)
            {
                Grid.Margin = new Thickness(X - 110, Y - 110, 0, 0);
                rotateTransfrom.Angle = Angle;
                image.RenderTransform = rotateTransfrom;
            }
        }

        public void Hide()
        {
            hideStoryboard.Begin();
        }
    }
}