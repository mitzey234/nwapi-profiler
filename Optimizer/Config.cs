namespace Optimizer
{
    public class Config
    {
        public int PlayerPositionUpdateRate { get; set; } = 45;

        public float ItemPickupServerUpdateRate { get; set; } = 0.25f;
		
        public int MaxExplosionsPerTick { get; set; } = 5;
		
        public int MaxExplosionPhysicsPerTick { get; set; } = 500;
		
        public int MaxPhysicsRange { get; set; } = 6;

    }
}
