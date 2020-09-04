﻿using TournamentAssistantShared.Database;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TournamentAssistantShared.Discord.Database
{
    [Table("Scores")]
    public class Score
    {
        [Column("ID")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Key]
        public ulong ID { get; set; }
        
        [Column("EventId")]
        public ulong EventId { get; set; }

        [Column("LevelId")]
        public string LevelId { get; set; }

        [Column("Score")]
        public int _Score { get; set; }

        [Column("Characteristic")]
        public string Characteristic { get; set; }

        [Column("BeatmapDifficulty")]
        public int BeatmapDifficulty { get; set; }

        [Column("GameOptions")]
        public int GameOptions { get; set; }

        [Column("PlayerOptions")]
        public int PlayerOptions { get; set; }

        [Column("Old")]
        public bool Old { get; set; }
    }
}