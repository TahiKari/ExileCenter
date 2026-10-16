namespace AmanamuVoidAlert

{

    using ExileCenter.Plugin;



    public sealed class AmanamuVoidAlertSettings : IPSettings

    {

        public bool EnableOverlay = true;



        public bool ShowDebugWindow;



        public bool DrawOnScreenLabels = true;



        public bool DrawOffscreenArrows = true;



        public bool DrawEdgeArrowForOnScreenMonsters;



        public bool DrawCircle = true;



        public bool OnlyRareOrUnique = true;



        public bool LogNewDetections;



        public float MaxDistance = 4000f;



        public float ForgetAfterSeconds = 12f;



        public float MissingEntityForgetSeconds = 1.5f;



        public float LabelYOffset = 70f;



        public float CircleRadius = 36f;



        public float ArrowEdgeMargin = 60f;



        public float ProxyDistance = 800f;

    }

}

