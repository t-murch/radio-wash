using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioWash.Api.Migrations
{
  /// <inheritdoc />
  public partial class CreateAuthUserTrigger : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      // Create function to handle new auth users
      migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.handle_new_auth_user()
                RETURNS TRIGGER
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = ''
                AS $$
                BEGIN
                    INSERT INTO public.""Users"" (""SupabaseId"", ""DisplayName"", ""Email"", ""CreatedAt"", ""UpdatedAt"")
                    VALUES (
                        NEW.id::text,
                        COALESCE(NEW.raw_user_meta_data ->> 'full_name', NEW.raw_user_meta_data ->> 'name', NEW.email),
                        NEW.email,
                        NOW(),
                        NOW()
                    );
                    RETURN NEW;
                END;
                $$;
            ");

      // Create trigger on auth.users table
      migrationBuilder.Sql(@"
                CREATE TRIGGER on_auth_user_created
                    AFTER INSERT ON auth.users
                    FOR EACH ROW
                    EXECUTE FUNCTION public.handle_new_auth_user();
            ");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      // Drop trigger
      migrationBuilder.Sql("DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;");

      // Drop function
      migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.handle_new_auth_user();");
    }
  }
}
