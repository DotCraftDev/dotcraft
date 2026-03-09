import type { Metadata } from "next";
import "./globals.css";
import "@copilotkitnext/react/styles.css";
import { AbortRejectionHandler } from "@/components/AbortRejectionHandler";

export const metadata: Metadata = {
  title: "DotCraft Chat",
  description: "Chat with DotCraft via AG-UI protocol",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body className="antialiased">
        <AbortRejectionHandler />
        {children}
      </body>
    </html>
  );
}
