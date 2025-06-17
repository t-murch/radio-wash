import { cookies } from 'next/headers';
import { NextResponse, type NextRequest } from 'next/server';

export async function middleware(request: NextRequest) {
  const response = NextResponse.next();
  const token = (await cookies()).get('rw-auth-token');
  console.log(`all cookies: ${JSON.stringify((await cookies()).getAll())}`);
  // console.log(
  //   `middleware token: ${JSON.stringify(token) ? 'DEFINED' : 'UNDEFINED'}`
  // );

  const publicPaths = ['/auth', '/auth/callback'];

  // if (
  //   !token &&
  //   request.nextUrl.pathname !== '/' &&
  //   !publicPaths.some((path) => request.nextUrl.pathname.startsWith(path))
  // ) {
  //   const url = request.nextUrl.clone();
  //   url.pathname = '/auth';
  //   return NextResponse.redirect(url);
  // }

  return response;
}

export const config = {
  matcher: [
    /*
     * Match all request paths except for the ones starting with:
     * - _next/static (static files)
     * - _next/image (image optimization files)
     * - favicon.ico (favicon file)
     * Feel free to modify this pattern to include more paths.
     */
    '/((?!_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp)$).*)',
  ],
};
