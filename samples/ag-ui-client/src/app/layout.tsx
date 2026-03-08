import type { Metadata } from "next";
import "./globals.css";
import "@copilotkitnext/react/styles.css";

export const metadata: Metadata = {
  title: "DotCraft AG-UI Client",
  description: "Chat with DotCraft via AG-UI protocol",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body className="antialiased">{children}</body>
    </html>
  );
}
