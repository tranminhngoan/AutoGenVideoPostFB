using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsStudio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiFilmFactory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FilmPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    CoverUrl = table.Column<string>(type: "text", nullable: true),
                    PrimaryGenre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VisualArtStyle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BrandColorHex = table.Column<string>(type: "text", nullable: false),
                    DefaultVoice = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    TargetAudience = table.Column<string>(type: "text", nullable: false),
                    AspectRatio = table.Column<string>(type: "text", nullable: false),
                    PublishingConfigJson = table.Column<string>(type: "text", nullable: true),
                    LinkedFacebookAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmPages_FacebookAccounts_LinkedFacebookAccountId",
                        column: x => x.LinkedFacebookAccountId,
                        principalTable: "FacebookAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FilmProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmPageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Logline = table.Column<string>(type: "text", nullable: false),
                    Genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VisualArtStyle = table.Column<string>(type: "text", nullable: false),
                    AspectRatio = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    TotalBudgetLimitUsd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    TotalActualCostUsd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    TargetEpisodes = table.Column<int>(type: "integer", nullable: false),
                    TargetDurationPerEpisodeSeconds = table.Column<int>(type: "integer", nullable: false),
                    VisualRatioAiVideo = table.Column<int>(type: "integer", nullable: false),
                    VisualRatioMotionImage = table.Column<int>(type: "integer", nullable: false),
                    VisualRatioMotionGraphics = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmProjects_FilmPages_FilmPageId",
                        column: x => x.FilmPageId,
                        principalTable: "FilmPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmCharacters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<string>(type: "text", nullable: false),
                    PhysicalAppearance = table.Column<string>(type: "text", nullable: false),
                    Hairstyle = table.Column<string>(type: "text", nullable: false),
                    DefaultCostume = table.Column<string>(type: "text", nullable: false),
                    Personality = table.Column<string>(type: "text", nullable: false),
                    EmotionalTrait = table.Column<string>(type: "text", nullable: false),
                    VoiceModel = table.Column<string>(type: "text", nullable: false),
                    ConsistencyPrompt = table.Column<string>(type: "text", nullable: false),
                    FrontReferenceImageUrl = table.Column<string>(type: "text", nullable: true),
                    SideReferenceImageUrl = table.Column<string>(type: "text", nullable: true),
                    FullBodyReferenceImageUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmCharacters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmCharacters_FilmProjects_FilmProjectId",
                        column: x => x.FilmProjectId,
                        principalTable: "FilmProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmContinuityRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    SceneNumber = table.Column<int>(type: "integer", nullable: false),
                    KeyDetailsJson = table.Column<string>(type: "text", nullable: false),
                    PotentialConflicts = table.Column<string>(type: "text", nullable: true),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmContinuityRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmContinuityRecords_FilmProjects_FilmProjectId",
                        column: x => x.FilmProjectId,
                        principalTable: "FilmProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Synopsis = table.Column<string>(type: "text", nullable: false),
                    OpeningHook = table.Column<string>(type: "text", nullable: false),
                    EndingCliffhanger = table.Column<string>(type: "text", nullable: false),
                    TargetDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ActualDurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    OutputVideoPath = table.Column<string>(type: "text", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "text", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    ActualCostUsd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    QualityScore = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmEpisodes_FilmProjects_FilmProjectId",
                        column: x => x.FilmProjectId,
                        principalTable: "FilmProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ArchitectureStyle = table.Column<string>(type: "text", nullable: false),
                    LightingMood = table.Column<string>(type: "text", nullable: false),
                    TimeOfDay = table.Column<string>(type: "text", nullable: false),
                    KeyProps = table.Column<string>(type: "text", nullable: false),
                    ReferenceImageUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmLocations_FilmProjects_FilmProjectId",
                        column: x => x.FilmProjectId,
                        principalTable: "FilmProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmStoryBibles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Logline = table.Column<string>(type: "text", nullable: false),
                    Genre = table.Column<string>(type: "text", nullable: false),
                    WorldRules = table.Column<string>(type: "text", nullable: false),
                    TimePeriod = table.Column<string>(type: "text", nullable: false),
                    GeographicSetting = table.Column<string>(type: "text", nullable: false),
                    MainConflict = table.Column<string>(type: "text", nullable: false),
                    KeyMysteriesAndTwists = table.Column<string>(type: "text", nullable: false),
                    ClimaxResolution = table.Column<string>(type: "text", nullable: false),
                    OverallArcOutline = table.Column<string>(type: "text", nullable: false),
                    EpisodesSummaryJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmStoryBibles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmStoryBibles_FilmProjects_FilmProjectId",
                        column: x => x.FilmProjectId,
                        principalTable: "FilmProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmCharacterReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmCharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Angle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    LocalFilePath = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmCharacterReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmCharacterReferences_FilmCharacters_FilmCharacterId",
                        column: x => x.FilmCharacterId,
                        principalTable: "FilmCharacters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmPublishingRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmEpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmPageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalPostId = table.Column<string>(type: "text", nullable: true),
                    ExternalVideoId = table.Column<string>(type: "text", nullable: true),
                    PostUrl = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmPublishingRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmPublishingRecords_FilmEpisodes_FilmEpisodeId",
                        column: x => x.FilmEpisodeId,
                        principalTable: "FilmEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilmPublishingRecords_FilmPages_FilmPageId",
                        column: x => x.FilmPageId,
                        principalTable: "FilmPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmScenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmEpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneNumber = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<string>(type: "text", nullable: false),
                    LocationName = table.Column<string>(type: "text", nullable: false),
                    ScenePurpose = table.Column<string>(type: "text", nullable: false),
                    TimeOfDay = table.Column<string>(type: "text", nullable: false),
                    CharactersPresentJson = table.Column<string>(type: "text", nullable: false),
                    NarrativeSummary = table.Column<string>(type: "text", nullable: false),
                    EstimatedDurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmScenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmScenes_FilmEpisodes_FilmEpisodeId",
                        column: x => x.FilmEpisodeId,
                        principalTable: "FilmEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmShots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmSceneId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmEpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShotNumber = table.Column<int>(type: "integer", nullable: false),
                    ShotType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CameraMovement = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VisualMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VisualPrompt = table.Column<string>(type: "text", nullable: false),
                    NegativePrompt = table.Column<string>(type: "text", nullable: true),
                    CharacterIdsPresent = table.Column<string>(type: "text", nullable: true),
                    SpeakerCharacterId = table.Column<string>(type: "text", nullable: true),
                    DialogueText = table.Column<string>(type: "text", nullable: true),
                    ActingInstructions = table.Column<string>(type: "text", nullable: true),
                    EmotionalTone = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    AudioVoicePath = table.Column<string>(type: "text", nullable: true),
                    AudioBgmPath = table.Column<string>(type: "text", nullable: true),
                    AudioSfxPath = table.Column<string>(type: "text", nullable: true),
                    RawImagePath = table.Column<string>(type: "text", nullable: true),
                    RenderedShotVideoPath = table.Column<string>(type: "text", nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    ActualCostUsd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    RenderStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmShots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmShots_FilmEpisodes_FilmEpisodeId",
                        column: x => x.FilmEpisodeId,
                        principalTable: "FilmEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilmShots_FilmScenes_FilmSceneId",
                        column: x => x.FilmSceneId,
                        principalTable: "FilmScenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmCostRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilmEpisodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    FilmShotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ServiceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    QuantityUnits = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    IsFreeTier = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmCostRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilmCostRecords_FilmEpisodes_FilmEpisodeId",
                        column: x => x.FilmEpisodeId,
                        principalTable: "FilmEpisodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FilmCostRecords_FilmProjects_FilmProjectId",
                        column: x => x.FilmProjectId,
                        principalTable: "FilmProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilmCostRecords_FilmShots_FilmShotId",
                        column: x => x.FilmShotId,
                        principalTable: "FilmShots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilmCharacterReferences_FilmCharacterId",
                table: "FilmCharacterReferences",
                column: "FilmCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmCharacters_FilmProjectId_CharacterId",
                table: "FilmCharacters",
                columns: new[] { "FilmProjectId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_FilmContinuityRecords_FilmProjectId_EpisodeNumber_SceneNumb~",
                table: "FilmContinuityRecords",
                columns: new[] { "FilmProjectId", "EpisodeNumber", "SceneNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_FilmCostRecords_CreatedAtUtc",
                table: "FilmCostRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FilmCostRecords_FilmEpisodeId",
                table: "FilmCostRecords",
                column: "FilmEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmCostRecords_FilmProjectId",
                table: "FilmCostRecords",
                column: "FilmProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmCostRecords_FilmShotId",
                table: "FilmCostRecords",
                column: "FilmShotId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmEpisodes_FilmProjectId_EpisodeNumber",
                table: "FilmEpisodes",
                columns: new[] { "FilmProjectId", "EpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_FilmLocations_FilmProjectId_LocationId",
                table: "FilmLocations",
                columns: new[] { "FilmProjectId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_FilmPages_LinkedFacebookAccountId",
                table: "FilmPages",
                column: "LinkedFacebookAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmPages_PageNumber",
                table: "FilmPages",
                column: "PageNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FilmProjects_CreatedAtUtc",
                table: "FilmProjects",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FilmProjects_FilmPageId",
                table: "FilmProjects",
                column: "FilmPageId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmProjects_Status",
                table: "FilmProjects",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FilmPublishingRecords_FilmEpisodeId_FilmPageId",
                table: "FilmPublishingRecords",
                columns: new[] { "FilmEpisodeId", "FilmPageId" });

            migrationBuilder.CreateIndex(
                name: "IX_FilmPublishingRecords_FilmPageId",
                table: "FilmPublishingRecords",
                column: "FilmPageId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmScenes_FilmEpisodeId_SceneNumber",
                table: "FilmScenes",
                columns: new[] { "FilmEpisodeId", "SceneNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_FilmShots_FilmEpisodeId",
                table: "FilmShots",
                column: "FilmEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmShots_FilmSceneId_ShotNumber",
                table: "FilmShots",
                columns: new[] { "FilmSceneId", "ShotNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_FilmStoryBibles_FilmProjectId",
                table: "FilmStoryBibles",
                column: "FilmProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilmCharacterReferences");

            migrationBuilder.DropTable(
                name: "FilmContinuityRecords");

            migrationBuilder.DropTable(
                name: "FilmCostRecords");

            migrationBuilder.DropTable(
                name: "FilmLocations");

            migrationBuilder.DropTable(
                name: "FilmPublishingRecords");

            migrationBuilder.DropTable(
                name: "FilmStoryBibles");

            migrationBuilder.DropTable(
                name: "FilmCharacters");

            migrationBuilder.DropTable(
                name: "FilmShots");

            migrationBuilder.DropTable(
                name: "FilmScenes");

            migrationBuilder.DropTable(
                name: "FilmEpisodes");

            migrationBuilder.DropTable(
                name: "FilmProjects");

            migrationBuilder.DropTable(
                name: "FilmPages");
        }
    }
}
